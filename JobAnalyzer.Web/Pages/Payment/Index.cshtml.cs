using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobAnalyzer.Web.Pages.Payment
{
    [Authorize]
    public class PaymentIndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public PaymentIndexModel(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public string PlanName { get; set; } = "";
        public decimal Amount { get; set; }
        public string? PaymentPageUrl { get; set; }
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string plan)
        {
            if (plan != "Pro" && plan != "Max")
                return RedirectToPage("/Pricing");

            PlanName = plan;
            Amount   = _config.GetValue<decimal>($"Pricing:{plan}");

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
            var email  = User.Identity?.Name ?? "";

            // PendingPayment kaydı oluştur
            var conversationId = Guid.NewGuid().ToString();
            _db.PendingPayments.Add(new PendingPayment
            {
                ConversationId = conversationId,
                UserId         = userId,
                Plan           = plan,
                Amount         = Amount,
                CreatedAt      = DateTime.UtcNow,
                IsProcessed    = false
            });
            await _db.SaveChangesAsync();

            // Iyzico ayarları
            var options = BuildIyzicoOptions();
            var callbackUrl = Environment.GetEnvironmentVariable("IYZICO_CALLBACK_URL")
                              ?? $"{Request.Scheme}://{Request.Host}/Payment/Callback";

            var request = new CreateCheckoutFormInitializeRequest
            {
                Locale          = Locale.TR.ToString(),
                ConversationId  = conversationId,
                Price           = Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                PaidPrice       = Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                Currency        = Currency.TRY.ToString(),
                BasketId        = conversationId,
                PaymentGroup    = PaymentGroup.PRODUCT.ToString(),
                CallbackUrl     = callbackUrl,
                Buyer = new Buyer
                {
                    Id            = userId,
                    Name          = User.Identity?.Name?.Split('@')[0] ?? "Kullanıcı",
                    Surname       = ".",
                    Email         = email,
                    IdentityNumber= "11111111111",  // Sandbox için placeholder
                    RegistrationAddress = "Türkiye",
                    City          = "Istanbul",
                    Country       = "Turkey"
                },
                ShippingAddress = new Address
                {
                    ContactName = User.Identity?.Name ?? "Kullanıcı",
                    City        = "Istanbul",
                    Country     = "Turkey",
                    Description = "Dijital ürün"
                },
                BillingAddress = new Address
                {
                    ContactName = User.Identity?.Name ?? "Kullanıcı",
                    City        = "Istanbul",
                    Country     = "Turkey",
                    Description = "Dijital ürün"
                },
                BasketItems = new List<BasketItem>
                {
                    new BasketItem
                    {
                        Id         = plan,
                        Name       = $"JobAnalyzer {plan} Plan (1 Ay)",
                        Category1  = "Yazılım",
                        ItemType   = BasketItemType.VIRTUAL.ToString(),
                        Price      = Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    }
                }
            };

            CheckoutFormInitialize form;
            try
            {
                form = await Task.Run(() => CheckoutFormInitialize.Create(request, options));
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ödeme başlatılamadı: {ex.Message}";
                return Page();
            }

            if (form.Status != "success")
            {
                ErrorMessage = $"Ödeme başlatılamadı: {form.ErrorMessage}";
                return Page();
            }

            PaymentPageUrl = form.PaymentPageUrl;
            return Page();
        }

        private Options BuildIyzicoOptions() => new Options
        {
            ApiKey    = Environment.GetEnvironmentVariable("IYZICO_API_KEY")    ?? "",
            SecretKey = Environment.GetEnvironmentVariable("IYZICO_SECRET_KEY") ?? "",
            BaseUrl   = Environment.GetEnvironmentVariable("IYZICO_BASE_URL")   ?? "https://sandbox-api.iyzipay.com"
        };
    }
}
