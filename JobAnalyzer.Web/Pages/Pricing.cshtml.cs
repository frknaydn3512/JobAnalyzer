using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobAnalyzer.Web.Pages
{
    public class PricingModel : PageModel
    {
        private readonly PlanService _planService;

        public PricingModel(PlanService planService)
        {
            _planService = planService;
        }

        public string CurrentPlan { get; set; } = "Free";
        public bool IsAuthenticated { get; set; }
        public string? Message { get; set; }

        public async Task OnGetAsync(string? msg)
        {
            IsAuthenticated = User.Identity?.IsAuthenticated == true;

            if (IsAuthenticated)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
                var plan = await _planService.GetCurrentPlanAsync(userId);
                CurrentPlan = plan.ToString();
            }

            Message = msg switch
            {
                "success" => "Ödeme başarılı! Planınız güncellendi.",
                "cancel"  => "Ödeme iptal edildi.",
                "error"   => "Ödeme sırasında bir hata oluştu. Lütfen tekrar deneyin.",
                "limit"   => "Plan limitinize ulaştınız. Devam etmek için planınızı yükseltin.",
                _         => null
            };
        }

        public async Task<IActionResult> OnPostCheckoutAsync(string plan)
        {
            if (User.Identity?.IsAuthenticated != true)
                return RedirectToPage("/Account/Login");

            if (plan != "Pro" && plan != "Max")
                return RedirectToPage("/Pricing");

            return RedirectToPage("/Payment/Index", new { plan });
        }
    }
}
