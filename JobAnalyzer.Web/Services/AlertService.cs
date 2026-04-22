using System.Net;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Services
{
    /// <summary>
    /// Hangfire tarafından günlük çağrılır.
    /// Her SavedSearch için yeni ilanları kontrol eder.
    /// </summary>
    public class AlertService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AlertService> _logger;
        private readonly PlanService _planService;

        public AlertService(AppDbContext db, IConfiguration config, ILogger<AlertService> logger, PlanService planService)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _planService = planService;
        }

        public async Task CheckAlertsAsync()
        {
            _logger.LogInformation("Alert kontrolü başladı: {Time}", DateTime.Now);

            var alerts = await _db.SavedSearches
                .Include(s => s.User)
                .ToListAsync();

            foreach (var alert in alerts)
            {
                try
                {
                    var since = alert.LastCheckedAt ?? alert.CreatedAt;
                    var query = _db.JobPostings.Where(j => j.DateScraped > since);

                    if (!string.IsNullOrEmpty(alert.Keyword))
                    {
                        var kw = alert.Keyword.ToLower();
                        query = query.Where(j =>
                            (j.Title != null && j.Title.ToLower().Contains(kw)) ||
                            (j.ExtractedSkills != null && j.ExtractedSkills.ToLower().Contains(kw)));
                    }

                    if (!string.IsNullOrEmpty(alert.LocationFilter))
                    {
                        var loc = alert.LocationFilter.ToLower();
                        query = query.Where(j => j.Location != null && j.Location.ToLower().Contains(loc));
                    }

                    var newJobs = await query.Take(50).ToListAsync();
                    alert.LastMatchCount = newJobs.Count;
                    alert.LastCheckedAt = DateTime.UtcNow;

                    if (newJobs.Count > 0 && alert.EmailEnabled && alert.User?.Email != null)
                    {
                        // Plan bazlı email alert kontrolü
                        var plan = await _planService.GetCurrentPlanAsync(alert.UserId);
                        var emailAllowed = JobAnalyzer.Data.PlanLimits.Config[plan].EmailAlerts;

                        if (emailAllowed)
                        {
                            await SendAlertEmailAsync(
                                alert.User.Email,
                                alert.Keyword ?? "iş ilanı",
                                newJobs.Count,
                                newJobs.Take(5).Select(j => $"&bull; {WebUtility.HtmlEncode(j.Title)} &mdash; {WebUtility.HtmlEncode(j.CompanyName)}").ToList()
                            );
                        }
                        else
                        {
                            _logger.LogInformation("Email atlanıldı (Free plan): UserId={UserId}", alert.UserId);
                        }
                    }

                    _logger.LogInformation("Alert '{Keyword}': {Count} yeni ilan", alert.Keyword, newJobs.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert {Id} hatası", alert.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Alert kontrolü tamamlandı.");
        }

        private async Task SendAlertEmailAsync(string toEmail, string keyword, int count, List<string> samples)
        {
            var smtpHost = _config["Email:SmtpHost"];
            if (string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogWarning("SMTP yapılandırması eksik. Email gönderilmedi.");
                return;
            }

            try
            {
                var smtpPort = _config.GetValue<int>("Email:SmtpPort", 587);
                var user = _config["Email:Username"] ?? "";
                var pass = _config["Email:Password"] ?? "";
                var from = _config["Email:From"] ?? user;

                using var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new System.Net.NetworkCredential(user, pass),
                    EnableSsl = true
                };

                string sampleHtml = string.Join("<br>", samples);
                string safeKeyword = WebUtility.HtmlEncode(keyword);
                string body = $@"
                    <h3>🔔 '{safeKeyword}' için {count} yeni ilan!</h3>
                    <p>JobAnalyzer'da arama kriterlerinize uygun yeni iş ilanları eklendi:</p>
                    <p>{sampleHtml}</p>
                    <a href='https://jobanalyzer.app/Listings?SearchTerm={Uri.EscapeDataString(keyword)}'
                       style='background:#0d6efd;color:white;padding:10px 20px;border-radius:8px;text-decoration:none'>
                       Tüm İlanları Gör →
                    </a>
                    <p style='color:#888;font-size:12px;margin-top:20px'>
                        Alert'i durdurmak için <a href='https://jobanalyzer.app/Account/Alerts'>buradan</a> yönetebilirsiniz.
                    </p>";

                var msg = new System.Net.Mail.MailMessage(from, toEmail)
                {
                    Subject = $"[JobAnalyzer] '{keyword}' için {count} yeni ilan",
                    Body = body,
                    IsBodyHtml = true
                };

                await client.SendMailAsync(msg);
                _logger.LogInformation("Email gönderildi: {To}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email gönderilemedi: {To}", toEmail);
            }
        }
    }
}
