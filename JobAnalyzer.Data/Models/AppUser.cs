using Microsoft.AspNetCore.Identity;

namespace JobAnalyzer.Data.Models
{
    public class AppUser : IdentityUser
    {
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public UserProfile? Profile { get; set; }
        public ICollection<SavedJob> SavedJobs { get; set; } = new List<SavedJob>();
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        public ICollection<UsageTracking> UsageTrackings { get; set; } = new List<UsageTracking>();
    }
}
