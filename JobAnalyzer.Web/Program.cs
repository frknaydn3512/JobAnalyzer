using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// .env dosyasını oku
DotNetEnv.Env.TraversePath().Load();

// DEFAULT_CONNECTION env var öncelikli, yoksa appsettings.json'dan oku
string connectionString =
    Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Veritabanı bağlantı dizesi bulunamadı. DEFAULT_CONNECTION env var'ı veya appsettings.json DefaultConnection ayarlayın.");

// ── 1. Razor Pages ────────────────────────────────────────────────────────
builder.Services.AddRazorPages();

// ── 2. Database + Identity ────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDefaultIdentity<AppUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;
    options.Password.RequireUppercase       = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars    = 4;
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts  = 5;
    options.Lockout.AllowedForNewUsers      = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();

// ── 3. Hangfire — arka plan iş kuyruğu ───────────────────────────────────
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

// Development'ta Hangfire worker başlatılmaz — sadece dashboard erişilebilir.
// Production'da (deploy sonrası) worker devreye girer ve cron job'lar çalışır.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHangfireServer();
}

// ── 4. Uygulama servisleri ────────────────────────────────────────────────
builder.Services.AddScoped<ScrapingOrchestrator>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<SubscriptionExpiryService>();
builder.Services.AddHttpClient();

// ── 5. Dosya yükleme limitleri (PDF — 10 MB) ──────────────────────────────
const long MaxFileSize = 10 * 1024 * 1024;

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit  = MaxFileSize;
    o.ValueLengthLimit          = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(o =>
    o.Limits.MaxRequestBodySize = MaxFileSize);

// ── 6. Cache ──────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();

// ── 7. Rate Limiting ──────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────
// Nginx arkasında gerçek istemci IP'lerini al (rate limiting için gerekli)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
    app.UseHttpsRedirection();
}

// ── Security Headers ──────────────────────────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire Dashboard (sadece Admin rolü görebilir - ilerleyen aşamada kısıtlanacak)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

app.MapRazorPages();

// ── Hangfire Recurring Jobs (sadece production'da kayıtlı) ───────────────
if (!app.Environment.IsDevelopment())
{
    var ignoreMisfire = new RecurringJobOptions
    {
        MisfireHandling = MisfireHandlingMode.Ignorable
    };

    RecurringJob.AddOrUpdate<ScrapingOrchestrator>(
        "daily-scraping",
        orchestrator => orchestrator.RunFullCycleAsync(),
        "0 3 * * *",
        ignoreMisfire
    );

    RecurringJob.AddOrUpdate<AlertService>(
        "daily-alerts",
        svc => svc.CheckAlertsAsync(),
        "0 9 * * *",
        ignoreMisfire
    );

    RecurringJob.AddOrUpdate<SubscriptionExpiryService>(
        "subscription-expiry",
        svc => svc.CheckExpiredSubscriptionsAsync(),
        "0 1 * * *",
        ignoreMisfire
    );
}

// ── Admin rolü seed ───────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Max", "Pro", "Free" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

app.Run();
