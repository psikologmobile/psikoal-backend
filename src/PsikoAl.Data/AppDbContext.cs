using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PsikoAl.Data.Entities;

namespace PsikoAl.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Expert> Experts => Set<Expert>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ExpertRating> ExpertRatings => Set<ExpertRating>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<Offer> Offers => Set<Offer>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<PushToken> PushTokens => Set<PushToken>();

    public DbSet<Assessment> Assessments => Set<Assessment>();

    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();

    public DbSet<AssessmentScoreRule> AssessmentScoreRules => Set<AssessmentScoreRule>();

    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<BlogLike> BlogLikes => Set<BlogLike>();

    public DbSet<PricingPeriod> PricingPeriods => Set<PricingPeriod>();

    public DbSet<WalletPayment> WalletPayments => Set<WalletPayment>();

    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    public DbSet<ListingClaim> ListingClaims => Set<ListingClaim>();



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Expert>(entity =>
        {
            entity.ToTable("experts");
            entity.HasKey(expert => expert.Id);
            entity.Property(expert => expert.Id).ValueGeneratedNever();
            entity.Property(expert => expert.PendingRevision).HasColumnType("jsonb");
            ConfigureStoreManaged(entity.Property(expert => expert.BalanceKurus));
            ConfigureStoreManaged(entity.Property(expert => expert.FreeOfferRights));
            // Kodu insert trigger'ı üretir (trg_experts_referral_code); EF yalnız geri okur.
            entity.Property(expert => expert.ReferralCode).ValueGeneratedOnAdd();
            entity.Property(expert => expert.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(expert => expert.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(expert => expert.Profile)
                .WithOne()
                .HasForeignKey<Expert>(expert => expert.Id);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(category => category.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("reviews");
            entity.HasKey(review => review.Id);
            entity.Property(review => review.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(review => review.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(review => review.Client)
                .WithMany()
                .HasForeignKey(review => review.ClientId);
            entity.HasOne<Match>()
                .WithMany()
                .HasForeignKey(review => review.MatchId);
        });

        modelBuilder.Entity<ExpertRating>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("expert_ratings");
        });

        modelBuilder.Entity<Listing>(entity =>
        {
            entity.ToTable("listings");
            entity.HasKey(listing => listing.Id);
            entity.Property(listing => listing.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(listing => listing.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(listing => listing.QualityScore).HasColumnType("numeric(4,3)");
            entity.Property(listing => listing.DailyDecayPct).HasColumnType("numeric(5,2)");
            entity.Property(listing => listing.PerOfferDecayPct).HasColumnType("numeric(5,2)");
            entity.HasOne(listing => listing.Client)
                .WithMany()
                .HasForeignKey(listing => listing.ClientId);
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("offers");
            entity.HasKey(offer => offer.Id);
            entity.Property(offer => offer.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(offer => offer.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(offer => offer.Listing)
                .WithMany()
                .HasForeignKey(offer => offer.ListingId);
            entity.HasOne(offer => offer.Expert)
                .WithMany()
                .HasForeignKey(offer => offer.ExpertId);
        });

        modelBuilder.Entity<Match>(entity =>
        {
            entity.ToTable("matches");
            entity.HasKey(match => match.Id);
            entity.Property(match => match.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(match => match.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(match => match.Listing)
                .WithMany()
                .HasForeignKey(match => match.ListingId);
            entity.HasOne(match => match.AcceptedOffer)
                .WithMany()
                .HasForeignKey(match => match.AcceptedOfferId);
            entity.HasOne(match => match.Client)
                .WithMany()
                .HasForeignKey(match => match.ClientId);
            entity.HasOne(match => match.Expert)
                .WithMany()
                .HasForeignKey(match => match.ExpertId);
        });

        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.ToTable("notification_templates");
            entity.HasKey(template => template.Id);
            entity.Property(template => template.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(template => template.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Data).HasColumnType("jsonb");
            entity.Property(notification => notification.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<PushToken>(entity =>
        {
            entity.ToTable("push_tokens");
            entity.HasKey(pushToken => pushToken.Id);
            entity.HasIndex(pushToken => pushToken.Token).IsUnique();
            entity.Property(pushToken => pushToken.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Assessment>(entity =>
        {
            entity.ToTable("assessments");
            entity.HasKey(assessment => assessment.Id);
            entity.Property(assessment => assessment.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(assessment => assessment.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<AssessmentQuestion>(entity =>
        {
            entity.ToTable("assessment_questions");
            entity.HasKey(question => question.Id);
            entity.Property(question => question.Options).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AssessmentScoreRule>(entity =>
        {
            entity.ToTable("assessment_score_rules");
            entity.HasKey(rule => rule.Id);
        });

        modelBuilder.Entity<AssessmentResult>(entity =>
        {
            entity.ToTable("assessment_results");
            entity.HasKey(result => result.Id);
            entity.Property(result => result.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(result => result.Assessment)
                .WithMany()
                .HasForeignKey(result => result.AssessmentId);
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("admin_users");
            entity.HasKey(adminUser => adminUser.Id);
            entity.HasIndex(adminUser => adminUser.AuthUserId).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(auditLog => auditLog.Id);
            entity.Property(auditLog => auditLog.OldValue).HasColumnType("jsonb");
            entity.Property(auditLog => auditLog.NewValue).HasColumnType("jsonb");
        });

        modelBuilder.Entity<Blog>(entity =>
        {
            entity.ToTable("blogs");
            entity.HasKey(blog => blog.Id);
            entity.HasIndex(blog => blog.Slug).IsUnique();
            entity.Property(blog => blog.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(blog => blog.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(blog => blog.Category)
                .WithMany()
                .HasForeignKey(blog => blog.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(blog => blog.Likes)
                .WithOne()
                .HasForeignKey(like => like.BlogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BlogLike>(entity =>
        {
            entity.ToTable("blog_likes");
            entity.HasKey(like => new { like.BlogId, like.UserId });
            entity.Property(like => like.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<PricingPeriod>(entity =>
        {
            entity.ToTable("pricing_periods");
            entity.HasKey(period => period.Id);
            entity.Property(period => period.QualityWeights).HasColumnType("jsonb");
            entity.Property(period => period.DailyDecayPct).HasColumnType("numeric(5,2)");
            entity.Property(period => period.PerOfferDecayPct).HasColumnType("numeric(5,2)");
            entity.Property(period => period.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(period => period.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(period => period.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WalletPayment>(entity =>
        {
            entity.ToTable("wallet_payments");
            entity.HasKey(payment => payment.Id);
            entity.HasIndex(payment => payment.MerchantOid).IsUnique();
            entity.Property(payment => payment.ProviderPayload).HasColumnType("jsonb");
            entity.Property(payment => payment.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(payment => payment.Expert)
                .WithMany()
                .HasForeignKey(payment => payment.ExpertId);
            entity.HasOne(payment => payment.InvoiceUploader)
                .WithMany()
                .HasForeignKey(payment => payment.InvoiceUploadedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("wallet_transactions");
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(transaction => transaction.Listing)
                .WithMany()
                .HasForeignKey(transaction => transaction.ListingId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Offer>()
                .WithMany()
                .HasForeignKey(transaction => transaction.OfferId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<WalletPayment>()
                .WithMany()
                .HasForeignKey(transaction => transaction.PaymentId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(transaction => transaction.AdminUser)
                .WithMany()
                .HasForeignKey(transaction => transaction.AdminUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ListingClaim>(entity =>
        {
            entity.ToTable("listing_claims");
            entity.HasKey(claim => claim.Id);
            entity.HasIndex(claim => new { claim.ListingId, claim.ExpertId }).IsUnique();
            entity.Property(claim => claim.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.HasOne(claim => claim.Listing)
                .WithMany()
                .HasForeignKey(claim => claim.ListingId);
            entity.HasOne(claim => claim.Expert)
                .WithMany()
                .HasForeignKey(claim => claim.ExpertId);
            entity.HasOne(claim => claim.Resolver)
                .WithMany()
                .HasForeignKey(claim => claim.ResolvedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    // Bakiye ve ücretsiz hak yalnız koşullu UPDATE ile değişir (ExpertWalletRepository). EF
    // bu kolonları ASLA yazmaz: izlenen bir Expert kaydedilirken bayat değer atomik düşümün
    // üzerine yazılamasın. Ekleme anında da gönderilmez; veritabanı varsayılanı (0) geçerlidir.
    private static void ConfigureStoreManaged(PropertyBuilder property)
    {
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
