namespace JobAnalyzer.Data.Models
{
    public enum SubscriptionPlan { Free, Pro, Max }

    public class Subscription
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Free;
        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public string? IyzicoOrderId { get; set; }
        public string? IyzicoPaymentId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public AppUser? User { get; set; }
    }
}
