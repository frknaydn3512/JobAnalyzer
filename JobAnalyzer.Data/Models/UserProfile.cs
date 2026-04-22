namespace JobAnalyzer.Data.Models
{
    public class UserProfile
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";

        // CV'den çıkarılan veya elle girilen bilgiler
        public string? ExtractedSkills { get; set; }   // CSV: "C#, .NET, React"
        public string? Level { get; set; }              // Junior / Mid / Senior
        public string? TargetLocation { get; set; }     // İstanbul, Uzaktan, vb.
        public int? ExpectedMinSalary { get; set; }
        public int? ExpectedMaxSalary { get; set; }
        public string? PreferredJobType { get; set; }   // "Remote" | "Hybrid" | "Onsite"
        public string? LastCvAnalysis { get; set; }     // JSON olarak son analiz sonucu
        public DateTime? LastAnalyzedAt { get; set; }

        // Navigation
        public AppUser? User { get; set; }
    }
}
