using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Services
{
    public class PlanService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public PlanService(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // Kullanıcının aktif planını döndürür (yoksa Free)
        public async Task<SubscriptionPlan> GetCurrentPlanAsync(string userId)
        {
            var sub = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.IsActive)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            return sub?.Plan ?? SubscriptionPlan.Free;
        }

        // Bu ayki UsageTracking satırını döndürür; yoksa oluşturur
        public async Task<UsageTracking> GetOrCreateUsageAsync(string userId)
        {
            var now = DateTime.UtcNow;
            var usage = await _db.UsageTrackings
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Year == now.Year && u.Month == now.Month);

            if (usage == null)
            {
                usage = new UsageTracking
                {
                    UserId = userId,
                    Year   = now.Year,
                    Month  = now.Month
                };
                _db.UsageTrackings.Add(usage);
                await _db.SaveChangesAsync();
            }

            return usage;
        }

        // CV analizi yapılabilir mi?
        public async Task<(bool Allowed, string Reason)> CanAnalyzeCvAsync(string userId)
        {
            var plan   = await GetCurrentPlanAsync(userId);
            var limit  = PlanLimits.Config[plan].CvAnalysesPerMonth;
            var usage  = await GetOrCreateUsageAsync(userId);

            if (usage.CvAnalysisCount >= limit)
            {
                return (false, $"Bu ay {limit} CV analiz hakkınızı kullandınız. Daha fazlası için planınızı yükseltin.");
            }

            return (true, "");
        }

        // CV analiz sayacını artırır
        public async Task IncrementCvAnalysisAsync(string userId)
        {
            var usage = await GetOrCreateUsageAsync(userId);
            usage.CvAnalysisCount++;
            await _db.SaveChangesAsync();
        }

        // Kapak mektubu oluşturulabilir mi?
        public async Task<(bool Allowed, string Reason)> CanGenerateCoverLetterAsync(string userId)
        {
            var plan  = await GetCurrentPlanAsync(userId);
            var limit = PlanLimits.Config[plan].CoverLettersPerMonth;

            if (limit == 0)
                return (false, "Kapak mektubu oluşturma Pro ve Max planlarda mevcuttur.");

            var usage = await GetOrCreateUsageAsync(userId);
            if (usage.CoverLetterCount >= limit)
                return (false, $"Bu ay {limit} kapak mektubu hakkınızı kullandınız.");

            return (true, "");
        }

        // Kapak mektubu sayacını artırır
        public async Task IncrementCoverLetterAsync(string userId)
        {
            var usage = await GetOrCreateUsageAsync(userId);
            usage.CoverLetterCount++;
            await _db.SaveChangesAsync();
        }

        // İş kaydedilebilir mi?
        public async Task<(bool Allowed, string Reason)> CanSaveJobAsync(string userId)
        {
            var plan  = await GetCurrentPlanAsync(userId);
            var limit = PlanLimits.Config[plan].SavedJobs;
            var count = await _db.SavedJobs.CountAsync(s => s.UserId == userId);

            if (count >= limit)
                return (false, $"Kayıtlı iş limitinize ({limit}) ulaştınız.");

            return (true, "");
        }

        // Arama kaydedilebilir mi?
        public async Task<(bool Allowed, string Reason)> CanSaveSearchAsync(string userId)
        {
            var plan  = await GetCurrentPlanAsync(userId);
            var limit = PlanLimits.Config[plan].SavedSearches;
            var count = await _db.SavedSearches.CountAsync(s => s.UserId == userId);

            if (count >= limit)
                return (false, $"Kayıtlı arama limitinize ({limit}) ulaştınız.");

            return (true, "");
        }

        // Ödeme sonrası subscription yükselt
        public async Task UpgradeSubscriptionAsync(string userId, SubscriptionPlan plan, string? iyzicoPaymentId = null, string? iyzicoOrderId = null)
        {
            // Mevcut aktif subscription'ları kapat
            var existing = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.IsActive)
                .ToListAsync();

            foreach (var s in existing)
            {
                s.IsActive = false;
                s.EndDate  = DateTime.UtcNow;
            }

            // Yeni subscription ekle
            _db.Subscriptions.Add(new Subscription
            {
                UserId          = userId,
                Plan            = plan,
                StartDate       = DateTime.UtcNow,
                EndDate         = DateTime.UtcNow.AddDays(30),
                IsActive        = true,
                IyzicoPaymentId = iyzicoPaymentId,
                IyzicoOrderId   = iyzicoOrderId
            });

            await _db.SaveChangesAsync();

            // Rolü güncelle
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.RemoveFromRolesAsync(user, new[] { "Free", "Pro", "Max" });
                await _userManager.AddToRoleAsync(user, plan.ToString());
            }
        }

        // Yeni kullanıcı için Free subscription oluştur
        public async Task CreateFreeSubscriptionAsync(string userId)
        {
            _db.Subscriptions.Add(new Subscription
            {
                UserId    = userId,
                Plan      = SubscriptionPlan.Free,
                StartDate = DateTime.UtcNow,
                EndDate   = null,   // Free plan süresi dolmaz
                IsActive  = true
            });
            await _db.SaveChangesAsync();
        }
    }
}
