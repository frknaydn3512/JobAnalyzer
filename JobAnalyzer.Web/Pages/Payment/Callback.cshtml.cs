using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using JobAnalyzer.Data;

namespace JobAnalyzer.Web.Pages.Payment
{
    // Iyzico POST ile çağırır — [IgnoreAntiforgeryToken] zorunlu
    [IgnoreAntiforgeryToken]
    public class CallbackModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly PlanService _planService;

        public CallbackModel(AppDbContext db, PlanService planService)
        {
            _db = db;
            _planService = planService;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var token = Request.Form["token"].ToString();

            if (string.IsNullOrEmpty(token))
                return RedirectToPage("/Payment/Cancel");

            var options = BuildIyzicoOptions();

            var request = new RetrieveCheckoutFormRequest
            {
                Locale = Locale.TR.ToString(),
                Token  = token
            };

            CheckoutForm form;
            try
            {
                form = await Task.Run(() => CheckoutForm.Retrieve(request, options));
            }
            catch
            {
                return RedirectToPage("/Payment/Cancel");
            }

            // Iyzico'dan dönen durum kontrolü
            if (form.Status != "success" || form.PaymentStatus != "SUCCESS")
                return RedirectToPage("/Payment/Cancel");

            var conversationId = form.ConversationId;

            // Atomic transaction: IsProcessed flag + plan upgrade tek bir işlemde
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Replay attack önleme — aynı conversation tekrar işlenmesin
                var pending = await _db.PendingPayments
                    .FirstOrDefaultAsync(p => p.ConversationId == conversationId && !p.IsProcessed);

                if (pending == null)
                {
                    await transaction.RollbackAsync();
                    return RedirectToPage("/Payment/Cancel");
                }

                pending.IsProcessed = true;
                await _db.SaveChangesAsync();

                // Plan yükselt
                var plan = pending.Plan == "Max" ? SubscriptionPlan.Max : SubscriptionPlan.Pro;
                await _planService.UpgradeSubscriptionAsync(
                    pending.UserId,
                    plan,
                    iyzicoPaymentId: form.PaymentId,
                    iyzicoOrderId:   conversationId
                );

                await transaction.CommitAsync();
                return RedirectToPage("/Payment/Success");
            }
            catch
            {
                await transaction.RollbackAsync();
                return RedirectToPage("/Payment/Cancel");
            }
        }

        private Options BuildIyzicoOptions() => new Options
        {
            ApiKey    = Environment.GetEnvironmentVariable("IYZICO_API_KEY")    ?? "",
            SecretKey = Environment.GetEnvironmentVariable("IYZICO_SECRET_KEY") ?? "",
            BaseUrl   = Environment.GetEnvironmentVariable("IYZICO_BASE_URL")   ?? "https://sandbox-api.iyzipay.com"
        };
    }
}
