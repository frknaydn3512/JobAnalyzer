namespace JobAnalyzer.Data.Models
{
    public class SavedJob
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public int JobPostingId { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public AppUser? User { get; set; }
        public JobPosting? JobPosting { get; set; }
    }
}
