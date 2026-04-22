using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Services
{
    public class SubscriptionExpiryService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public SubscriptionExpiryService(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // Hangfire tarafından her gün 01:00'de çalıştırılır
        public async Task CheckExpiredSubscriptionsAsync()
        {
            var now = DateTime.UtcNow;

            var expired = await _db.Subscriptions
                .Where(s => s.IsActive && s.EndDate.HasValue && s.EndDate < now && s.Plan != SubscriptionPlan.Free)
                .ToListAsync();

            foreach (var sub in expired)
            {
                sub.IsActive = false;

                // Free subscription ekle
                _db.Subscriptions.Add(new Subscription
                {
                    UserId    = sub.UserId,
                    Plan      = SubscriptionPlan.Free,
                    StartDate = now,
                    EndDate   = null,
                    IsActive  = true
                });

                // Rolü Free'e düşür
                var user = await _userManager.FindByIdAsync(sub.UserId);
                if (user != null)
                {
                    await _userManager.RemoveFromRolesAsync(user, new[] { "Pro", "Max" });
                    await _userManager.AddToRoleAsync(user, "Free");
                }
            }

            if (expired.Count > 0)
                await _db.SaveChangesAsync();
        }
    }
}
