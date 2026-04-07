using System;

namespace JobAnalyzer.Data.Models
{
    public class JobPosting
    {
        public int Id { get; set; }
        public string? Title { get; set; } 
        public string? CompanyName { get; set; } 
        public string? Location { get; set; } 
        public string? Description { get; set; } 
        public string? Url { get; set; } 
        public string? Source { get; set; } 
        public DateTime? DatePosted { get; set; } 
        public DateTime DateScraped { get; set; } 
        public string? ExtractedSkills { get; set; }
        public string? Level { get; set; }
        public int? MinSalary { get; set; }
        public int? MaxSalary { get; set; }
        public string? Currency { get; set; } = "TL";
        public string? JobType { get; set; } = "Şirket İlanı";
    }
}