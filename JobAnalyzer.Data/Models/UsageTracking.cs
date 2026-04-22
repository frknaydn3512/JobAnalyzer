namespace JobAnalyzer.Data.Models
{
    public class UsageTracking
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public int Year { get; set; }
        public int Month { get; set; }
        public int CvAnalysisCount { get; set; } = 0;
        public int CoverLetterCount { get; set; } = 0;

        public AppUser? User { get; set; }
    }
}
