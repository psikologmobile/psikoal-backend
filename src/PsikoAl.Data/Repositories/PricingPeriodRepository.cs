using Microsoft.EntityFrameworkCore;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Data.Repositories;

public sealed class PricingPeriodRepository(AppDbContext dbContext)
    : Repository<PricingPeriod, Guid>(dbContext), IPricingPeriodRepository
{
    public Task<PricingPeriod?> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => DbContext.PricingPeriods
            .AsNoTracking()
            .Where(period => period.EffectiveFrom <= now)
            .OrderByDescending(period => period.EffectiveFrom)
            .ThenByDescending(period => period.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
