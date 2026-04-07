using JobAnalyzer.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// .env dosyasını oku (üst dizinlerdeki .env dosyalarını arayıp yükler)
DotNetEnv.Env.TraversePath().Load();

// 1. Razor Pages
builder.Services.AddRazorPages();

// 2. Database Bağlantısı
string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. HttpClient
builder.Services.AddHttpClient();

// 4. Form ve Kestrel Limitleri (PDF yüklemeleri için - bunların ikisi birlikte ayarlanMALI)
const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxFileSize;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = MaxFileSize;
});

var app = builder.Build();

// HATA YAKALAMA
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.Run();