using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Listing.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Listings;

// İlana bağlanan test sonucu, ilanı gören uzmanlara gösteriliyor. Sahiplik kontrolü
// yokken başkasının sonucunun id'si ilana iliştirilip sağlık verisi ifşa edilebiliyordu.
public sealed class ListingAssessmentOwnershipTests
{
    [Fact]
    public async Task Someone_elses_result_is_rejected()
    {
        using var database = new TestDatabase();
        await database.AddPricingPeriodAsync();
        var client = await database.AddProfileAsync();
        var other = await database.AddProfileAsync();
        var foreignResult = await database.AddAssessmentResultAsync(other.Id);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService(database).CreateAsync(client.Id, NewRequest(foreignResult.Id), CancellationToken.None));

        Assert.Equal(ErrorKeys.AssessmentResultNotOwned, exception.ErrorKey);
        Assert.Equal("assessmentResultId", exception.Field);
        Assert.Empty(database.Context.Listings);
    }

    [Fact]
    public async Task Anonymous_result_is_rejected()
    {
        using var database = new TestDatabase();
        await database.AddPricingPeriodAsync();
        var client = await database.AddProfileAsync();
        var anonymousResult = await database.AddAssessmentResultAsync(userId: null);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService(database).CreateAsync(client.Id, NewRequest(anonymousResult.Id), CancellationToken.None));

        Assert.Equal(ErrorKeys.AssessmentResultNotOwned, exception.ErrorKey);
    }

    [Fact]
    public async Task Unknown_result_id_is_rejected()
    {
        using var database = new TestDatabase();
        await database.AddPricingPeriodAsync();
        var client = await database.AddProfileAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateService(database).CreateAsync(client.Id, NewRequest(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(ErrorKeys.AssessmentResultNotOwned, exception.ErrorKey);
    }

    [Fact]
    public async Task Own_result_is_attached()
    {
        using var database = new TestDatabase();
        await database.AddPricingPeriodAsync();
        database.Context.SystemSettings.Add(new SystemSetting { Key = SystemSettingKeys.ListingAutoApprove, Value = "false" });
        var client = await database.AddProfileAsync();
        var ownResult = await database.AddAssessmentResultAsync(client.Id);

        var listing = await CreateService(database).CreateAsync(client.Id, NewRequest(ownResult.Id), CancellationToken.None);

        Assert.Equal(ownResult.Id, Assert.Single(database.Context.Listings).AssessmentResultId);
        Assert.Equal(ListingStatuses.PendingApproval, listing.Status);
    }

    private static ListingService CreateService(TestDatabase database)
        => new(database.UnitOfWork, new AllCategoriesExistStub(), Substitute.For<PsikoAl.Services.Abstractions.INotificationService>(), TimeProvider.System);

    private static CreateListingDto NewRequest(Guid assessmentResultId)
        => new("Kaygı için destek", "Açıklama", ["Kaygı"], 500, 900, "online", City: null, assessmentResultId, ContactConsent: true, Phone: "05551112233");
}
