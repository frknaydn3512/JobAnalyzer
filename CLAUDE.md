# JobAnalyzer — Claude Context

## Proje Özeti
İş ilanı scraper + analiz platformu. Türkiye odaklı, Supabase (PostgreSQL) tabanlı, ASP.NET Core 10 Razor Pages.

## Mimari (3 proje)
```
JobAnalyzer.Data/      → EF Core modeller, AppDbContext, migrations
JobAnalyzer.Scraper/   → 20+ scraper (konsol uygulaması), GroqAnalyzer
JobAnalyzer.Web/       → Razor Pages web uygulaması, Hangfire, servisler
```

## Tech Stack
- **Framework**: ASP.NET Core 10.0 Razor Pages
- **ORM**: EF Core 10 + **Npgsql** (PostgreSQL — Supabase)
- **Auth**: ASP.NET Identity + roller: `Admin`, `Max`, `Pro`, `Free`
- **Background jobs**: Hangfire 1.8 + **Hangfire.PostgreSql**
- **AI**: Groq LLaMA-3.3-70B (`GROQ_API_KEY`)
- **Scraping**: Playwright 1.58 + PuppeteerSharp 24 + HtmlAgilityPack
- **PDF**: UglyToad.PdfPig

## Veritabanı Bağlantısı
- **Connection string**: `DEFAULT_CONNECTION` env var (`.env` dosyasından okunur)
- `.env` → DotNetEnv ile yüklenir (`Program.cs` line 14)
- `appsettings.json` fallback olarak çalışır (placeholder değer)
- Supabase proje ref: **kullanıcı dolduracak**

## Önemli Dosyalar
| Dosya | Açıklama |
|---|---|
| `JobAnalyzer.Web/Program.cs` | Servis kayıtları, Hangfire, rol seed |
| `JobAnalyzer.Data/AppDbContext.cs` | EF Core context |
| `JobAnalyzer.Data/Models/` | AppUser, JobPosting, UserProfile, SavedJob, SavedSearch |
| `JobAnalyzer.Web/Services/ScrapingOrchestrator.cs` | Gece 03:00 scraping döngüsü |
| `JobAnalyzer.Web/Services/AlertService.cs` | Sabah 09:00 alert e-postası |
| `JobAnalyzer.Scraper/Scrapers/ScraperBase.cs` | Tüm scraper'ların base sınıfı |
| `JobAnalyzer.Scraper/GroqAnalyzer.cs` | AI skill extraction |
| `.env` | API anahtarları (git'e commit edilmez) |

## Mevcut Modeller (DB Tabloları)
- `JobPostings` — iş ilanları (Title, CompanyName, Location, Description, Url, Source, ExtractedSkills, Level, MinSalary, MaxSalary, JobType)
- `AspNetUsers` + Identity tabloları — kullanıcılar
- `UserProfiles` — kullanıcı kariyer profili (1-to-1 AppUser)
- `SavedJobs` — kullanıcının kaydettiği ilanlar
- `SavedSearches` — kullanıcının kayıtlı aramaları/alertleri

## Sayfa Yapısı
```
/              → Dashboard (istatistikler, grafikler)
/Listings      → İlan listesi (arama + filtre + sayfalama)
/JobDetail     → İlan detayı + kapak mektubu üretimi
/CvAnalyzer    → CV yükle → AI analiz → iş eşleştirme
/SalaryBenchmark → Maaş verileri
/Pricing       → Plan karşılaştırma (henüz yok — Phase 3)
/Payment/      → Iyzico ödeme akışı (henüz yok — Phase 3)
/Account/      → Login, Register, Profile, SavedJobs, Alerts
/Admin/        → Hangfire dashboard, istatistikler
```

## Aktif Geliştirme Planı
Detaylı plan: `C:\Users\FURKAN\.claude\plans\foamy-sparking-hedgehog.md`

### Tamamlanan
- [x] **Phase 1**: SQL Server → Supabase/PostgreSQL migration
  - NuGet: `Npgsql.EntityFrameworkCore.PostgreSQL` v10.0.1, `Hangfire.PostgreSql` v1.21.1
  - Tüm scraper'lar `UseNpgsql` kullanıyor
  - `DEFAULT_CONNECTION` env var ile bağlantı

### Bekleyen
- [ ] **Migrations**: Eski SQL Server migration'ları silinecek → `InitialPostgres` oluşturulacak (önce Supabase URL .env'e yazılmalı)
- [ ] **Phase 2**: Subscription, UsageTracking, PendingPayment modelleri + PlanService
- [ ] **Phase 3**: Iyzico ödeme (Pro: 199 TL/ay, Max: 399 TL/ay)
- [ ] **Phase 4**: Plan kısıtlama enforcement
- [ ] **Phase 5**: CV analizörü iyileştirmeleri (IDF ağırlıklı eşleştirme, salary filtre)

## Plan Limitleri (hedef)
| Özellik | Free | Pro | Max |
|---|---|---|---|
| CV analiz/ay | 3 | 20 | Sınırsız |
| İş eşleştirme | 3 | 10 | 20 |
| Kayıtlı iş | 10 | 50 | Sınırsız |
| Kayıtlı arama | 2 | 10 | Sınırsız |
| E-posta alert | ✗ | ✓ | ✓ |
| Kapak mektubu/ay | 0 | 5 | Sınırsız |

## Sık Kullanılan Komutlar
```bash
# Build
dotnet build

# Migration oluştur (Phase 1 bittikten sonra)
dotnet ef migrations add <MigrationName> --project JobAnalyzer.Data --startup-project JobAnalyzer.Web

# Migration uygula
dotnet ef database update --project JobAnalyzer.Data --startup-project JobAnalyzer.Web

# Web uygulamasını çalıştır
dotnet run --project JobAnalyzer.Web

# Scraper çalıştır (interaktif menü)
dotnet run --project JobAnalyzer.Scraper
```

## Önemli Kurallar
- Connection string **her zaman** `DEFAULT_CONNECTION` env var'dan okunur
- Scraper'lar `ScraperBase` sınıfından türer; connection string oradan gelir
- `UseSqlServer` kullanılmaz — her yerde `UseNpgsql`
- `.env` dosyası git'e commit edilmez
- Her phase bittikten sonra kullanıcıdan onay beklenir
