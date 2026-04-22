# JobAnalyzer AI

Yapay zeka destekli iş ilanı toplama, analiz ve kariyer koçluğu platformu. 20+ kaynaktan iş ilanı toplayıp AI ile analiz eder, CV eşleştirmesi yapar ve kapak mektubu üretir.

## Özellikler

- **20+ Scraper** — Kariyer.net, LinkedIn, Indeed, RemoteOK, Remotive, Himalayas, Adzuna, Jooble, WeWorkRemotely, Bionluk, Freelancer.com ve daha fazlası
- **AI Skill Extraction** — Groq LLaMA-3.3-70B ile iş ilanlarından teknik beceri çıkarma
- **CV Analizi** — PDF yükle, AI ile analiz et, iş ilanlarıyla eşleştir
- **Kapak Mektubu** — İlana özel AI destekli kapak mektubu üretimi
- **Job Alert** — Kayıtlı aramalara e-posta bildirimi (günlük/haftalık)
- **Maaş Benchmark** — Pozisyon ve teknolojiye göre maaş karşılaştırması
- **Abonelik Sistemi** — Free / Pro / Max planları, Iyzico ödeme entegrasyonu
- **Admin Paneli** — Hangfire dashboard, istatistikler, scraping yönetimi

## Tech Stack

| Katman | Teknoloji |
|--------|-----------|
| Framework | ASP.NET Core 10.0 Razor Pages |
| Veritabanı | PostgreSQL (Supabase) |
| ORM | Entity Framework Core 10 + Npgsql |
| Kimlik Doğrulama | ASP.NET Identity (Admin, Free, Pro, Max rolleri) |
| Arka Plan İşleri | Hangfire 1.8 + Hangfire.PostgreSql |
| AI | Groq API (LLaMA-3.3-70B) |
| Scraping | Playwright 1.58, PuppeteerSharp 24, HtmlAgilityPack |
| Ödeme | Iyzipay 2.1 |
| PDF İşleme | UglyToad.PdfPig |

## Proje Yapısı

```
JobAnalyzer/
├── JobAnalyzer.Data/           # EF Core modeller, AppDbContext, migrations
│   ├── Models/
│   │   ├── AppUser.cs          # Identity kullanıcı modeli
│   │   ├── JobPosting.cs       # İş ilanı modeli
│   │   ├── UserProfile.cs      # Kullanıcı kariyer profili
│   │   ├── SavedJob.cs         # Kayıtlı ilanlar
│   │   ├── SavedSearch.cs      # Kayıtlı aramalar/alertler
│   │   ├── Subscription.cs     # Abonelik bilgisi
│   │   ├── UsageTracking.cs    # Aylık kullanım takibi
│   │   └── PendingPayment.cs   # Bekleyen ödemeler
│   └── AppDbContext.cs
│
├── JobAnalyzer.Scraper/        # Konsol uygulaması — scraper'lar + AI analiz
│   ├── Scrapers/
│   │   ├── ScraperBase.cs      # Tüm scraper'ların base sınıfı
│   │   ├── IJobScraper.cs      # Scraper interface'i
│   │   ├── KariyerNetScraper.cs
│   │   ├── LinkedInScraper.cs
│   │   ├── RemotiveScraper.cs
│   │   └── ... (20+ scraper)
│   ├── GroqAnalyzer.cs         # AI skill extraction
│   └── SkillNormalizer.cs      # Beceri normalizasyonu
│
├── JobAnalyzer.Web/            # Razor Pages web uygulaması
│   ├── Pages/
│   │   ├── Index.cshtml        # Dashboard (grafikler, istatistikler)
│   │   ├── Listings.cshtml     # İlan listesi (arama + filtre + sayfalama)
│   │   ├── JobDetail.cshtml    # İlan detayı + kapak mektubu
│   │   ├── CvAnalyzer.cshtml   # CV yükle → AI analiz → iş eşleştirme
│   │   ├── SalaryBenchmark.cshtml # Maaş karşılaştırma
│   │   ├── Pricing.cshtml      # Plan karşılaştırma
│   │   ├── Account/            # Login, Register, Profile, Alerts, SavedJobs
│   │   ├── Payment/            # Iyzico ödeme akışı
│   │   └── Admin/              # Admin paneli
│   └── Services/
│       ├── ScrapingOrchestrator.cs  # Otomatik scraping döngüsü
│       ├── AlertService.cs          # E-posta alert servisi
│       ├── PlanService.cs           # Abonelik yönetimi
│       └── SubscriptionExpiryService.cs
│
├── .env.example                # Ortam değişkenleri şablonu
└── README.md
```

## Kurulum

### Gereksinimler

- .NET 10.0 SDK
- PostgreSQL veritabanı (veya Supabase hesabı)
- Groq API anahtarı

### Adımlar

1. **Projeyi klonlayın:**
   ```bash
   git clone <repo-url>
   cd JobAnalyzer
   ```

2. **Ortam değişkenlerini ayarlayın:**
   ```bash
   cp .env.example .env
   ```
   `.env` dosyasını açıp gerçek değerleri girin.

3. **Bağımlılıkları yükleyin:**
   ```bash
   dotnet restore
   ```

4. **Veritabanı migration'ı çalıştırın:**
   ```bash
   dotnet ef database update --project JobAnalyzer.Data --startup-project JobAnalyzer.Web
   ```

5. **Web uygulamasını başlatın:**
   ```bash
   dotnet run --project JobAnalyzer.Web
   ```

6. **(Opsiyonel) Scraper'ı çalıştırın:**
   ```bash
   dotnet run --project JobAnalyzer.Scraper
   ```

## Ortam Değişkenleri

`.env` dosyasına eklenmesi gereken değişkenler:

| Değişken | Açıklama | Zorunlu |
|----------|----------|---------|
| `DEFAULT_CONNECTION` | PostgreSQL bağlantı dizesi | Evet |
| `GROQ_API_KEY` | Groq API anahtarı (AI analiz) | Evet |
| `ADZUNA_APP_ID` | Adzuna API uygulama ID'si | Hayır |
| `ADZUNA_APP_KEY` | Adzuna API anahtarı | Hayır |
| `JOOBLE_API_KEY` | Jooble API anahtarı | Hayır |
| `SERPAPI_KEY` | SerpAPI anahtarı (Google Jobs) | Hayır |
| `RAPIDAPI_KEY` | RapidAPI anahtarı (JSearch) | Hayır |
| `IYZICO_API_KEY` | Iyzico API anahtarı | Hayır |
| `IYZICO_SECRET_KEY` | Iyzico gizli anahtarı | Hayır |
| `IYZICO_BASE_URL` | Iyzico base URL (sandbox/prod) | Hayır |

## Sayfa Yapısı

| Yol | Açıklama |
|-----|----------|
| `/` | Dashboard — istatistikler, grafikler |
| `/Listings` | İlan listesi — arama, filtre, sayfalama |
| `/JobDetail?id=N` | İlan detayı + kapak mektubu üretimi |
| `/CvAnalyzer` | CV yükle → AI analiz → iş eşleştirme |
| `/SalaryBenchmark` | Maaş verileri |
| `/Pricing` | Plan karşılaştırma |
| `/Account/Login` | Giriş |
| `/Account/Register` | Kayıt |
| `/Account/Profile` | Profil düzenleme |
| `/Account/SavedJobs` | Kayıtlı ilanlar |
| `/Account/Alerts` | Job alert yönetimi |
| `/Admin` | Admin paneli (Admin rolü gerekli) |
| `/hangfire` | Hangfire dashboard (Admin rolü gerekli) |

## Plan Limitleri

| Özellik | Free | Pro (199 TL/ay) | Max (399 TL/ay) |
|---------|------|-----------------|-----------------|
| CV analiz / ay | 3 | 20 | Sınırsız |
| İş eşleştirme | 3 | 10 | 20 |
| Kayıtlı iş | 10 | 50 | Sınırsız |
| Kayıtlı arama | 2 | 10 | Sınırsız |
| E-posta alert | Yok | Var | Var |
| Kapak mektubu / ay | 0 | 5 | Sınırsız |

## Zamanlı Görevler (Hangfire)

| İş | Zamanlama | Açıklama |
|----|-----------|----------|
| `daily-scraping` | Her gece 03:00 | Tüm scraper'ları çalıştır + AI analiz |
| `daily-alerts` | Her sabah 09:00 | Kayıtlı aramalara e-posta gönder |
| `subscription-expiry` | Her gece 01:00 | Süresi dolan abonelikleri kontrol et |

## Geliştirme Komutları

```bash
# Build
dotnet build

# Web uygulamasını çalıştır
dotnet run --project JobAnalyzer.Web

# Scraper'ı çalıştır (interaktif menü)
dotnet run --project JobAnalyzer.Scraper

# Migration oluştur
dotnet ef migrations add <MigrationName> --project JobAnalyzer.Data --startup-project JobAnalyzer.Web

# Migration uygula
dotnet ef database update --project JobAnalyzer.Data --startup-project JobAnalyzer.Web
```

## Güvenlik

- ASP.NET Core Identity ile kimlik doğrulama ve rol tabanlı yetkilendirme
- CSRF koruması (AntiForgeryToken) tüm POST formlarında
- Rate limiting (brute force koruması)
- Account lockout (5 başarısız giriş → 15 dk kilitleme)
- Güvenlik header'ları (X-Content-Type-Options, X-Frame-Options, Referrer-Policy)
- CDN kaynakları SRI (Subresource Integrity) hash'leri ile doğrulanır
- Tüm API anahtarları `.env` dosyasında (git'e commit edilmez)
- EF Core parametreli sorgular (SQL injection koruması)
- AI prompt injection koruması (system message + input sanitization)

## Lisans

Bu proje kişisel kullanım içindir.
