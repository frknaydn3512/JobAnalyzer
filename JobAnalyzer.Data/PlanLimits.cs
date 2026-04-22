using JobAnalyzer.Data.Models;

namespace JobAnalyzer.Data
{
    public record PlanConfig(
        int CvAnalysesPerMonth,
        int JobMatches,
        int SavedSearches,
        int SavedJobs,
        bool EmailAlerts,
        int CoverLettersPerMonth
    );

    public static class PlanLimits
    {
        public static readonly Dictionary<SubscriptionPlan, PlanConfig> Config = new()
        {
            [SubscriptionPlan.Free] = new(
                CvAnalysesPerMonth:   3,
                JobMatches:           3,
                SavedSearches:        2,
                SavedJobs:            10,
                EmailAlerts:          false,
                CoverLettersPerMonth: 0
            ),
            [SubscriptionPlan.Pro] = new(
                CvAnalysesPerMonth:   20,
                JobMatches:           10,
                SavedSearches:        10,
                SavedJobs:            50,
                EmailAlerts:          true,
                CoverLettersPerMonth: 5
            ),
            [SubscriptionPlan.Max] = new(
                CvAnalysesPerMonth:   int.MaxValue,
                JobMatches:           20,
                SavedSearches:        int.MaxValue,
                SavedJobs:            int.MaxValue,
                EmailAlerts:          true,
                CoverLettersPerMonth: int.MaxValue
            ),
        };
    }
}
