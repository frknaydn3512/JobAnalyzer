namespace JobAnalyzer.Data.Models
{
    public class SavedSearch
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public string? Keyword { get; set; }
        public string? LocationFilter { get; set; }
        public string? SourceFilter { get; set; }
        public string Frequency { get; set; } = "daily"; // "daily" | "weekly"
        public bool EmailEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastCheckedAt { get; set; }
        public int LastMatchCount { get; set; } = 0;

        // Navigation
        public AppUser? User { get; set; }
    }
}
