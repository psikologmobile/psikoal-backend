using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PsikoAl.Data;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories;

namespace PsikoAl.Services.Tests.Support;

// EF InMemory: servis kurallarını (sahiplik, durum geçişleri) gerçek repository ve
// UnitOfWork üzerinden ölçer. Postgres'e özgü davranış (trigger, RPC, ExecuteUpdate)
// burada YOKTUR — onlar E2E katmanının işi.
public sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Context = new AppDbContext(options);
        UnitOfWork = new UnitOfWork(Context);
    }

    public AppDbContext Context { get; }

    public UnitOfWork UnitOfWork { get; }

    public async Task<Profile> AddProfileAsync(string role = "client", string status = "active")
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.test",
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            Role = role,
            Status = status,
        };

        Context.Profiles.Add(profile);
        await Context.SaveChangesAsync();
        return profile;
    }

    public async Task<AssessmentResult> AddAssessmentResultAsync(Guid? userId)
    {
        var assessment = new Assessment { Id = Guid.NewGuid(), Title = "Kaygı", Category = "kaygi" };
        var result = new AssessmentResult
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessment.Id,
            UserId = userId,
            Score = 7,
            Level = "moderate",
            Summary = "Orta",
        };

        Context.Assessments.Add(assessment);
        Context.AssessmentResults.Add(result);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return result;
    }

    // Revize 2: ilan ve teklif akışları geçerli fiyatlandırma dönemini okur (başlangıç dönemi).
    public async Task<PricingPeriod> AddPricingPeriodAsync()
    {
        var period = new PricingPeriod
        {
            Id = Guid.NewGuid(),
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            BudgetMinTry = 500,
            FeeMinTry = 250,
            FeeMaxTry = 1000,
            DailyDecayPct = 10,
            PerOfferDecayPct = 10,
            QualityWeights = PsikoAl.Services.Pricing.QualityWeights.Default.ToJson(),
        };

        Context.PricingPeriods.Add(period);
        await Context.SaveChangesAsync();
        return period;
    }

    public void Dispose() => Context.Dispose();
}
