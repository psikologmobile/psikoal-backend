using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Services.Tests.Support;

/// <summary>EF InMemory + bellek içi cüzdan.</summary>
/// <remarks>
/// Bakiye/hak değişimi gerçek ortamda ham SQL'dir (koşullu UPDATE ... RETURNING) ve
/// InMemory sağlayıcıda koşamaz. Bu sınıf diğer bütün repository'leri gerçek InMemory
/// UnitOfWork'e bırakır, yalnız ExpertWallets'ı aynı kuralı (eksiye düşemez) uygulayan
/// bir sahteyle değiştirir. Koşullu UPDATE'in eşzamanlılık yarısı E2E katmanında ölçülür.
/// </remarks>
public sealed class WalletTestDatabase : IUnitOfWork, IDisposable
{
    private readonly TestDatabase _database = new();

    public FakeExpertWalletRepository Wallets { get; } = new();

    public TestDatabase Database => _database;

    public Data.AppDbContext Context => _database.Context;

    private UnitOfWork Inner => _database.UnitOfWork;

    public IProfileRepository Profiles => Inner.Profiles;

    public IExpertRepository Experts => Inner.Experts;

    public ICategoryRepository Categories => Inner.Categories;

    public IReviewRepository Reviews => Inner.Reviews;

    public IListingRepository Listings => Inner.Listings;

    public IOfferRepository Offers => Inner.Offers;

    public IMatchRepository Matches => Inner.Matches;

    public INotificationTemplateRepository NotificationTemplates => Inner.NotificationTemplates;

    public INotificationRepository Notifications => Inner.Notifications;

    public IPushTokenRepository PushTokens => Inner.PushTokens;

    public IAssessmentRepository Assessments => Inner.Assessments;

    public IAssessmentQuestionRepository AssessmentQuestions => Inner.AssessmentQuestions;

    public IAssessmentScoreRuleRepository AssessmentScoreRules => Inner.AssessmentScoreRules;

    public IAssessmentResultRepository AssessmentResults => Inner.AssessmentResults;

    public ISystemSettingRepository SystemSettings => Inner.SystemSettings;

    public IAdminUserRepository AdminUsers => Inner.AdminUsers;

    public IAuditLogRepository AuditLogs => Inner.AuditLogs;

    public IBlogRepository Blogs => Inner.Blogs;

    public IBlogLikeRepository BlogLikes => Inner.BlogLikes;

    public IPricingPeriodRepository PricingPeriods => Inner.PricingPeriods;

    public IWalletPaymentRepository WalletPayments => Inner.WalletPayments;

    public IWalletTransactionRepository WalletTransactions => Inner.WalletTransactions;

    public IExpertWalletRepository ExpertWallets => Wallets;

    public IListingClaimRepository ListingClaims => Inner.ListingClaims;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Inner.SaveChangesAsync(cancellationToken);

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => Inner.BeginTransactionAsync(cancellationToken);

    public async Task<Expert> AddExpertAsync(string status = "approved", long balanceKurus = 0, int freeRights = 0, Guid? referredBy = null)
    {
        var profile = await _database.AddProfileAsync(role: "expert");
        profile.ReferredByExpertId = referredBy;
        var expert = new Expert { Id = profile.Id, Title = "Klinik Psikolog", Status = status, ReferralCode = NewCode() };
        Context.Experts.Add(expert);
        await Context.SaveChangesAsync();
        Wallets.Set(expert.Id, balanceKurus, freeRights);
        return expert;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        Context.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        await Context.SaveChangesAsync();
    }

    public void Dispose() => _database.Dispose();

    private static string NewCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}

public sealed class FakeExpertWalletRepository : IExpertWalletRepository
{
    private readonly Dictionary<Guid, WalletState> _states = [];

    public void Set(Guid expertId, long balanceKurus, int freeRights) => _states[expertId] = new WalletState(balanceKurus, freeRights);

    public WalletState StateOf(Guid expertId) => _states[expertId];

    public Task<WalletState?> GetAsync(Guid expertId, CancellationToken cancellationToken)
        => Task.FromResult(_states.TryGetValue(expertId, out var state) ? state : null);

    public Task<WalletState?> TryApplyAsync(Guid expertId, long deltaKurus, int deltaRights, CancellationToken cancellationToken)
    {
        if (!_states.TryGetValue(expertId, out var state)
            || state.BalanceKurus + deltaKurus < 0
            || state.FreeOfferRights + deltaRights < 0)
        {
            return Task.FromResult<WalletState?>(null);
        }

        var next = new WalletState(state.BalanceKurus + deltaKurus, state.FreeOfferRights + deltaRights);
        _states[expertId] = next;
        return Task.FromResult<WalletState?>(next);
    }
}
