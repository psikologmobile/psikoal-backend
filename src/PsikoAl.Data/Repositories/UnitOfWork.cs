using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Data.Repositories;

public sealed class UnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    private IProfileRepository? _profiles;
    private IExpertRepository? _experts;
    private ICategoryRepository? _categories;
    private IReviewRepository? _reviews;
    private IListingRepository? _listings;
    private IOfferRepository? _offers;
    private IMatchRepository? _matches;
    private INotificationTemplateRepository? _notificationTemplates;
    private INotificationRepository? _notifications;
    private IPushTokenRepository? _pushTokens;
    private IAssessmentRepository? _assessments;
    private IAssessmentQuestionRepository? _assessmentQuestions;
    private IAssessmentScoreRuleRepository? _assessmentScoreRules;
    private IAssessmentResultRepository? _assessmentResults;
    private ISystemSettingRepository? _systemSettings;
    private IAdminUserRepository? _adminUsers;
    private IAuditLogRepository? _auditLogs;
    private IBlogRepository? _blogs;
    private IBlogLikeRepository? _blogLikes;
    private IPricingPeriodRepository? _pricingPeriods;
    private IWalletPaymentRepository? _walletPayments;
    private IWalletTransactionRepository? _walletTransactions;
    private IExpertWalletRepository? _expertWallets;
    private IListingClaimRepository? _listingClaims;

    public IProfileRepository Profiles => _profiles ??= new ProfileRepository(dbContext);

    public IExpertRepository Experts => _experts ??= new ExpertRepository(dbContext);

    public ICategoryRepository Categories => _categories ??= new CategoryRepository(dbContext);

    public IReviewRepository Reviews => _reviews ??= new ReviewRepository(dbContext);

    public IListingRepository Listings => _listings ??= new ListingRepository(dbContext);

    public IOfferRepository Offers => _offers ??= new OfferRepository(dbContext);

    public IMatchRepository Matches => _matches ??= new MatchRepository(dbContext);

    public INotificationTemplateRepository NotificationTemplates
        => _notificationTemplates ??= new NotificationTemplateRepository(dbContext);

    public INotificationRepository Notifications => _notifications ??= new NotificationRepository(dbContext);

    public IPushTokenRepository PushTokens => _pushTokens ??= new PushTokenRepository(dbContext);

    public IAssessmentRepository Assessments => _assessments ??= new AssessmentRepository(dbContext);

    public IAssessmentQuestionRepository AssessmentQuestions
        => _assessmentQuestions ??= new AssessmentQuestionRepository(dbContext);

    public IAssessmentScoreRuleRepository AssessmentScoreRules
        => _assessmentScoreRules ??= new AssessmentScoreRuleRepository(dbContext);

    public IAssessmentResultRepository AssessmentResults
        => _assessmentResults ??= new AssessmentResultRepository(dbContext);

    public ISystemSettingRepository SystemSettings => _systemSettings ??= new SystemSettingRepository(dbContext);

    public IAdminUserRepository AdminUsers => _adminUsers ??= new AdminUserRepository(dbContext);

    public IAuditLogRepository AuditLogs => _auditLogs ??= new AuditLogRepository(dbContext);

    public IBlogRepository Blogs => _blogs ??= new BlogRepository(dbContext);

    public IBlogLikeRepository BlogLikes => _blogLikes ??= new BlogLikeRepository(dbContext);

    public IPricingPeriodRepository PricingPeriods => _pricingPeriods ??= new PricingPeriodRepository(dbContext);

    public IWalletPaymentRepository WalletPayments => _walletPayments ??= new WalletPaymentRepository(dbContext);

    public IWalletTransactionRepository WalletTransactions
        => _walletTransactions ??= new WalletTransactionRepository(dbContext);

    public IExpertWalletRepository ExpertWallets => _expertWallets ??= new ExpertWalletRepository(dbContext);

    public IListingClaimRepository ListingClaims => _listingClaims ??= new ListingClaimRepository(dbContext);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new UnitOfWorkTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));
}
