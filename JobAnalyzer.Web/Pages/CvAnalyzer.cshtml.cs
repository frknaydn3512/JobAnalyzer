using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Features;


namespace JobAnalyzer.Web.Pages
{
    [RequestSizeLimit(10 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public class CvAnalyzerModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserManager<AppUser> _userManager;
        private readonly PlanService _planService;
        private readonly string _groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        public CvAnalyzerModel(AppDbContext context, IHttpClientFactory httpClientFactory, UserManager<AppUser> userManager, PlanService planService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _planService = planService;
        }

        [BindProperty]
        public IFormFile? UploadedCv { get; set; }

        public CvResult? AnalysisResult { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LimitMessage { get; set; }
        public int RemainingAnalyses { get; set; } = -1;  // -1 = sınırsız

        public List<JobMatchResult> RecommendedJobs { get; set; } = new();
        public bool ShowUpsell { get; set; }

        public async Task OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User)!;
                var plan  = await _planService.GetCurrentPlanAsync(userId);
                var usage = await _planService.GetOrCreateUsageAsync(userId);
                var limit = JobAnalyzer.Data.PlanLimits.Config[plan].CvAnalysesPerMonth;
                RemainingAnalyses = limit == int.MaxValue ? -1 : Math.Max(0, limit - usage.CvAnalysisCount);
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Plan limiti kontrolü (giriş yapmış kullanıcılar için)
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User)!;
                var (allowed, reason) = await _planService.CanAnalyzeCvAsync(userId);
                if (!allowed)
                {
                    LimitMessage = reason;
                    await OnGetAsync();
                    return Page();
                }
            }

            if (UploadedCv == null || UploadedCv.Length == 0)
            {
                ErrorMessage = "Lütfen geçerli bir PDF dosyası seçin.";
                return Page();
            }

            if (!UploadedCv.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                && !UploadedCv.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Yalnızca PDF dosyaları kabul edilmektedir.";
                return Page();
            }

            try
            {
                string cvText = "";
                byte[] pdfBytes;
                using (var ms = new MemoryStream())
                {
                    await UploadedCv.CopyToAsync(ms);
                    pdfBytes = ms.ToArray();
                }

                // Magic bytes kontrolü: gerçek PDF dosyası mı? (%PDF = 0x25 0x50 0x44 0x46)
                if (pdfBytes.Length < 4 ||
                    pdfBytes[0] != 0x25 || pdfBytes[1] != 0x50 ||
                    pdfBytes[2] != 0x44 || pdfBytes[3] != 0x46)
                {
                    ErrorMessage = "Geçersiz dosya. Lütfen gerçek bir PDF yükleyin.";
                    return Page();
                }

                using (var document = PdfDocument.Open(pdfBytes))
                {
                    var sb = new StringBuilder();
                    foreach (var page in document.GetPages())
                    {
                        sb.Append(page.Text);
                        sb.Append(' ');
                    }
                    cvText = sb.ToString();
                }

                if (string.IsNullOrWhiteSpace(cvText))
                    throw new Exception("PDF metni okunamadı. Dosya taranmış bir resim olabilir.");

                var rawSkills = await _context.JobPostings
                    .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills))
                    .AsNoTracking()
                    .Select(j => j.ExtractedSkills)
                    .ToListAsync();

                var topSkills = rawSkills
                    .Where(s => s != null)
                    .SelectMany(s => s!.Split(','))
                    .GroupBy(s => s.Trim())
                    .OrderByDescending(g => g.Count())
                    .Take(25)
                    .Select(g => g.Key)
                    .ToList();

                string marketTrendStr = string.Join(", ", topSkills);

                // 6. AI Analizini Başlat
                AnalysisResult = await GetAiAnalysis(cvText, marketTrendStr);

                // 7. Eşleştirme
                if (AnalysisResult != null && !string.IsNullOrEmpty(AnalysisResult.MySkills))
                {
                    string? userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

                    int matchLimit = 5;
                    UserProfile? profile = null;
                    if (userId != null)
                    {
                        var plan = await _planService.GetCurrentPlanAsync(userId);
                        matchLimit = JobAnalyzer.Data.PlanLimits.Config[plan].JobMatches;
                        profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

                        // Free kullanıcıya upsell göster
                        ShowUpsell = plan == JobAnalyzer.Data.Models.SubscriptionPlan.Free;
                    }
                    await FindMatches(AnalysisResult.MySkills, matchLimit, profile);

                    // 8. Kullanım sayacını artır ve profili kaydet
                    if (userId != null)
                    {
                        await _planService.IncrementCvAnalysisAsync(userId);

                        if (profile == null)
                        {
                            profile = new UserProfile { UserId = userId };
                            _context.UserProfiles.Add(profile);
                        }
                        profile.ExtractedSkills = AnalysisResult.MySkills;
                        profile.Level = AnalysisResult.Level;
                        profile.LastCvAnalysis = AnalysisResult.Advice;
                        profile.LastAnalyzedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }
                await OnGetAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Analiz başarısız: {ex.Message}";
                Console.WriteLine($"!!! KRİTİK HATA: {ex}");
            }

            return Page();
        }

        private async Task FindMatches(string userSkills, int limit, UserProfile? profile)
        {
            var userSkillList = userSkills.Split(',')
                .Select(s => s.Trim().ToLower())
                .Where(s => s.Length > 0)
                .ToList();

            // AsNoTracking: salt-okunur işlem, change tracking yükü yok
            // Son 60 günün ilanları ile sınırla — IDF hesabı için yeterli örneklem
            var cutoffLoad = DateTime.UtcNow.AddDays(-60);
            var allJobs = await _context.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills) && j.DateScraped >= cutoffLoad)
                .AsNoTracking()
                .ToListAsync();

            int totalJobs = allJobs.Count;
            if (totalJobs == 0) return;

            // IDF: her skill kaç ilanda geçiyor?
            var skillFrequency = allJobs
                .SelectMany(j => j.ExtractedSkills!.Split(',').Select(s => s.Trim().ToLower()))
                .GroupBy(s => s)
                .ToDictionary(g => g.Key, g => g.Count());

            var cutoff7Days = DateTime.UtcNow.AddDays(-7);

            var results = allJobs
                .Select(j =>
                {
                    var jobSkills = j.ExtractedSkills!.Split(',')
                        .Select(s => s.Trim().ToLower())
                        .Where(s => s.Length > 0)
                        .ToList();

                    var matched = jobSkills.Intersect(userSkillList).ToList();
                    if (matched.Count == 0) return null;

                    // IDF ağırlıklı puan
                    double score = matched.Sum(skill =>
                    {
                        int freq = skillFrequency.TryGetValue(skill, out int f) ? f : 1;
                        return Math.Log((double)totalJobs / (freq + 1) + 1);
                    });

                    // Recency bonus
                    bool isRecent = j.DateScraped >= cutoff7Days;
                    if (isRecent) score *= 1.2;

                    int matchPct = jobSkills.Count > 0
                        ? (int)Math.Round((double)matched.Count / jobSkills.Count * 100)
                        : 0;

                    return new JobMatchResult
                    {
                        Job               = j,
                        MatchedSkillCount = matched.Count,
                        TotalJobSkillCount = jobSkills.Count,
                        MatchPercentage   = matchPct,
                        WeightedScore     = score,
                        MatchedSkills     = matched,
                        IsRecent          = isRecent
                    };
                })
                .Where(r => r != null)
                .Select(r => r!)
                .AsEnumerable();

            // Salary filtresi
            if (profile?.ExpectedMinSalary > 0)
            {
                results = results.Where(r =>
                    r.Job.MaxSalary == null || r.Job.MaxSalary == 0 ||
                    r.Job.MaxSalary >= profile.ExpectedMinSalary);
            }

            // JobType filtresi
            if (!string.IsNullOrEmpty(profile?.PreferredJobType) && profile.PreferredJobType != "Any")
            {
                var pref = profile.PreferredJobType.ToLower();
                results = results.Where(r =>
                    string.IsNullOrEmpty(r.Job.JobType) ||
                    r.Job.JobType.ToLower().Contains(pref));
            }

            RecommendedJobs = results
                .OrderByDescending(r => r.WeightedScore)
                .Take(limit)
                .ToList();
        }

        private async Task<CvResult> GetAiAnalysis(string cvText, string topMarketSkills)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_groqKey}");

            var requestObj = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[] {
                    new { role = "system", content = "Sen profesyonel bir İK ve teknik yetenek analistisin. Sadece JSON döneceksin." },
                    new { role = "user", content = $"Aşağıdaki CV'yi piyasa trendleriyle kıyasla. CV'deki projelere bakarak tecrübe seviyesini (Junior, Mid veya Senior) tahmin et. Ayrıca 2026 Türkiye yazılım piyasası şartlarına göre bu adayın hak ettiği tahmini aylık net maaş aralığını belirle. \nTrendler: {topMarketSkills} \nCV: {cvText} \n\nYanıt formatı tam olarak şu JSON olmalı: {{ \"MySkills\": \"Yetenekler\", \"MissingSkills\": [\"Eksik1\"], \"Advice\": \"Öneri\", \"Level\": \"Tahmin Edilen Seviye\", \"EstimatedSalary\": \"Örn: 80.000 TL - 110.000 TL\" }}" }
                },
                temperature = 0.2
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestObj), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                var resBody = await response.Content.ReadAsStringAsync();
                dynamic? jsonRes = JsonConvert.DeserializeObject(resBody);

                if (jsonRes == null)
                    throw new Exception("Groq API'den boş yanıt geldi.");

                string rawAiText = jsonRes.choices[0].message.content;

                if (rawAiText.Contains("{"))
                {
                    int start = rawAiText.IndexOf('{');
                    int end = rawAiText.LastIndexOf('}');
                    if (end > start)
                        rawAiText = rawAiText.Substring(start, end - start + 1);
                }

                return JsonConvert.DeserializeObject<CvResult>(rawAiText)
                    ?? throw new Exception("AI yanıtı parse edilemedi.");
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API hatası: {response.StatusCode} - {errorBody}");
        }
    }

    public class CvResult
    {
        public string? MySkills { get; set; }
        public List<string>? MissingSkills { get; set; }
        public string? Advice { get; set; }
        public string? Level { get; set; }
        public string? EstimatedSalary { get; set; }
    }

    public class JobMatchResult
    {
        public JobAnalyzer.Data.Models.JobPosting Job { get; set; } = null!;
        public int MatchedSkillCount { get; set; }
        public int TotalJobSkillCount { get; set; }
        public int MatchPercentage { get; set; }
        public double WeightedScore { get; set; }
        public List<string> MatchedSkills { get; set; } = new();
        public bool IsRecent { get; set; }
    }
}