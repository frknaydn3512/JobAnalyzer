namespace JobAnalyzer.Data.Models
{
    public class PendingPayment
    {
        public int Id { get; set; }
        public string ConversationId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Plan { get; set; } = "";   // "Pro" | "Max"
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsProcessed { get; set; } = false;
    }
}
