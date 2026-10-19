using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Experts;

// REVIZE-2 §6: danışan bir uzman profilini yalnız (a) uzman ilanına teklif verdiyse ya da
// (b) o uzmanın davetiyle geldiyse görür; aksi 404 EXPERT_NOT_FOUND.
public sealed class ExpertVisibilityTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Unrelated_client_cannot_see_the_expert()
    {
        var expertId = Guid.NewGuid();
        var client = await _db.AddProfileAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => ExpertVisibility.EnsureVisibleAsync(_db.UnitOfWork, expertId, client.Id, CancellationToken.None));

        Assert.Equal(ErrorKeys.ExpertNotFound, exception.ErrorKey);
    }

    [Fact]
    public async Task Client_sees_an_expert_who_offered_on_their_listing()
    {
        var expert = await _db.AddProfileAsync(role: "expert");
        var client = await _db.AddProfileAsync();
        var listing = new Listing { Id = Guid.NewGuid(), ClientId = client.Id, Title = "İlan başlığı", PreferredSessionType = "online" };
        _db.Context.Listings.Add(listing);
        _db.Context.Offers.Add(new Offer { Id = Guid.NewGuid(), ListingId = listing.Id, ExpertId = expert.Id, Price = 1, SessionType = "online" });
        await _db.Context.SaveChangesAsync();

        await ExpertVisibility.EnsureVisibleAsync(_db.UnitOfWork, expert.Id, client.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Client_sees_the_expert_who_invited_them()
    {
        var expertId = Guid.NewGuid();
        var client = await _db.AddProfileAsync();
        client.ReferredByExpertId = expertId;
        await _db.Context.SaveChangesAsync();

        await ExpertVisibility.EnsureVisibleAsync(_db.UnitOfWork, expertId, client.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Experts_and_the_owner_keep_the_old_rule()
    {
        var expertId = Guid.NewGuid();
        var otherExpert = await _db.AddProfileAsync(role: "expert");

        await ExpertVisibility.EnsureVisibleAsync(_db.UnitOfWork, expertId, otherExpert.Id, CancellationToken.None);
        await ExpertVisibility.EnsureVisibleAsync(_db.UnitOfWork, expertId, expertId, CancellationToken.None);
    }

    public void Dispose() => _db.Dispose();
}
