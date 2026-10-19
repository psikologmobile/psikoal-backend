using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using PsikoAl.Api.Authorization;
using PsikoAl.Api.BackgroundServices;
using PsikoAl.Api.Extensions;
using PsikoAl.Api.Middleware;
using PsikoAl.Api.Serialization;
using PsikoAl.Data;
using PsikoAl.Data.Repositories;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Notifications;
using PsikoAl.Services.Options;
using PsikoAl.Services.Payments;

var builder = WebApplication.CreateBuilder(args);

// DİKKAT — öncelik sırası: bu satır CreateBuilder'dan SONRA geldiği için
// appsettings.Local.json, user-secrets'ı EZER. Normal geliştirme akışı user-secrets'tır
// (bkz. appsettings.Local.example.json); bu dosya yalnız container/mount senaryosu için
// bir kaçış kapısı olarak duruyor. Düz metin prod anahtarı ASLA buraya yazılmaz.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName))
    .ValidateOnStart();

builder.Services
    .AddOptions<BrevoOptions>()
    .Bind(builder.Configuration.GetSection(BrevoOptions.SectionName));

builder.Services
    .AddOptions<LegalOptions>()
    .Bind(builder.Configuration.GetSection(LegalOptions.SectionName));

builder.Services
    .AddOptions<PayTrOptions>()
    .Bind(builder.Configuration.GetSection(PayTrOptions.SectionName));

builder.Services
    .AddOptions<SiteOptions>()
    .Bind(builder.Configuration.GetSection(SiteOptions.SectionName));

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));

var supabase = builder.Configuration.GetSection(SupabaseOptions.SectionName).Get<SupabaseOptions>();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }
});

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHttpClient<ISupabaseAuthService, SupabaseAuthService>(client =>
{
    if (supabase is null)
    {
        return;
    }

    client.BaseAddress = new Uri(supabase.Url.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabase.AnonKey);
});

builder.Services.AddHttpClient<ISupabaseAdminService, SupabaseAdminService>(client =>
{
    if (supabase is null)
    {
        return;
    }

    client.BaseAddress = new Uri(supabase.Url.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabase.ServiceRoleKey);
    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + supabase.ServiceRoleKey);
});

builder.Services.AddHttpClient<ISupabaseStorageService, SupabaseStorageService>(client =>
{
    if (supabase is null)
    {
        return;
    }

    client.BaseAddress = new Uri(supabase.Url.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabase.ServiceRoleKey);
    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + supabase.ServiceRoleKey);
});

builder.Services.AddHttpClient<IEmailService, BrevoEmailService>(client =>
{
    client.BaseAddress = new Uri("https://api.brevo.com/");
});

// Push artık arka planda gidiyor (PushDispatchWorker) ama Expo yanıt vermezse işçi
// kilitlenmemeli: deneme ve toplam süre sınırlı. POST tekrar DENENMEZ — Expo isteği
// almış ama yanıtı kaybolmuşsa kullanıcıya aynı bildirim iki kez giderdi.
builder.Services.AddHttpClient<IPushNotificationService, ExpoPushNotificationService>(client =>
{
    client.BaseAddress = new Uri("https://exp.host/");
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.Retry.DisableForUnsafeHttpMethods();
    });

// PayTR get-token: yanıt gelmezse ödeme sayfası sonsuza dek beklemesin. POST tekrar
// DENENMEZ — PayTR token üretmiş ama yanıt kaybolmuşsa aynı oid ikinci kez gönderilirdi.
// Belge: https://dev.paytr.com/iframe-api/iframe-api-1-adim
builder.Services.AddHttpClient<IPayTrClient, PayTrClient>(client =>
{
    client.BaseAddress = new Uri("https://www.paytr.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddSingleton<IPushOutbox, PushOutbox>();
builder.Services.AddScoped<PushDispatcher>();
builder.Services.AddHostedService<PushDispatchWorker>();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAccountStatusService, AccountStatusService>();
builder.Services.AddScoped<IAccountErasureService, AccountErasureService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IExpertService, ExpertService>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminExpertService, AdminExpertService>();
builder.Services.AddScoped<IAdminCategoryService, AdminCategoryService>();
builder.Services.AddScoped<IAdminReviewService, AdminReviewService>();
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<IAdminListingService, AdminListingService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IOfferService, OfferService>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IAdminMatchService, AdminMatchService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushTokenService, PushTokenService>();
builder.Services.AddScoped<IAdminNotificationTemplateService, AdminNotificationTemplateService>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<IAdminAssessmentService, AdminAssessmentService>();
builder.Services.AddScoped<IBlogService, BlogService>();
builder.Services.AddScoped<IAdminBlogService, AdminBlogService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IWalletPaymentService, WalletPaymentService>();
builder.Services.AddScoped<IListingClaimService, ListingClaimService>();
builder.Services.AddScoped<IAdminPricingService, AdminPricingService>();
builder.Services.AddScoped<IAdminClaimService, AdminClaimService>();
builder.Services.AddScoped<IAdminReferralService, AdminReferralService>();
builder.Services.AddScoped<IAdminActivityService, AdminActivityService>();
builder.Services.AddScoped<IAdminWalletService, AdminWalletService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAdminGuard, AdminGuard>();
builder.Services.AddScoped<IAdminAuditLogService, AdminAuditLogService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AdminRequirementHandler>();

// Supabase artık JWT'leri asimetrik anahtarlarla (ES256) imzalıyor ve JWKS/OIDC discovery yayınlıyor;
// paylaşılan "JWT Secret" (legacy HS256) yerine standart Authority tabanlı doğrulama kullanılır.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (supabase is not null)
        {
            options.Authority = supabase.Url.TrimEnd('/') + "/auth/v1";
        }

        // Lokal Supabase stack'i (supabase start) http konuşur: Authority
        // http://127.0.0.1:54321/auth/v1 olur. RequireHttpsMetadata varsayılan olarak
        // açık olduğundan JwtBearer'ın kendi PostConfigure adımı istisna fırlatır ve
        // /health dahil TÜM istekler 500'e düşer — yani API lokal stack'e karşı hiç
        // ayağa kalkamaz. Bu, geliştirme ve E2E için zorunlu bir kaçış kapısı.
        //
        // Yalnız Development ortamında gevşetilir; Staging/Production'da açık kalır.
        // ASPNETCORE_ENVIRONMENT prod'da asla Development olmamalı (bu kural
        // docs/DEVOPS-PSIKOAL.md, deploy adımının doğrulama listesinde).
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudiences = ["authenticated"],
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization(options => options.AddAdminPolicies());

builder.Services.AddConfiguredRateLimiting(builder.Configuration);

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter()));
builder.Services.AddConfiguredForwardedHeaders(builder.Configuration);
// Web sitesi (ayrı köken) API'yi tarayıcıdan çağırır; izinli kökenler Cors:AllowedOrigins.
builder.Services.AddConfiguredCors();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadinessTag]);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<DomainExceptionMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// Kimlik doğrulamadan ve rate limit'ten ÖNCE: preflight (OPTIONS) token taşımaz, burada
// yanıtlanır. Origin göndermeyen istemciler (mobil uygulama, PayTR callback) etkilenmez.
app.UseCors(CorsSetup.PolicyName);
app.UseAuthentication();
app.UseMiddleware<AccountStatusMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
// /health: hazırlık (DB dahil) — yük dengeleyici ve E2E betikleri bunu bekler.
// /health/live: yalnız sürecin ayakta olduğu; DB kesintisinde container yeniden başlatılmasın.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program
{
    private const string ReadinessTag = "ready";
}
