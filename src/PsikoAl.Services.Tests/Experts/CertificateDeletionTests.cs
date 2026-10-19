using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Experts;

// DELETE /experts/profile/certificates: uzman yalnız kendi listesindeki bir değeri siler.
// Storage silmesi service_role ile yapıldığı için yalnız uzmanın kendi klasöründeki obje
// silinir; serbest metin döneminden kalan değerler yalnız listeden düşer.
public sealed class CertificateDeletionTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeStorageService _storage = new();

    [Fact]
    public async Task Removes_the_certificate_from_the_list_and_deletes_its_storage_object()
    {
        var expert = await AddExpertAsync(cert => [cert("a"), cert("b")]);
        var target = $"{expert.Id}/certificates/a.pdf";

        var remaining = await Service().DeleteCertificateAsync(expert.Id, target, CancellationToken.None);

        Assert.Equal([$"{expert.Id}/certificates/b.pdf"], remaining);
        Assert.Equal([$"{expert.Id}/certificates/b.pdf"], (await _db.Context.Experts.FindAsync(expert.Id))!.Certificates);
        Assert.Equal(("documents", target), Assert.Single(_storage.DeletedObjects));
    }

    [Fact]
    public async Task Unknown_certificate_is_404_and_changes_nothing()
    {
        var expert = await AddExpertAsync(cert => [cert("a")]);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeleteCertificateAsync(expert.Id, $"{expert.Id}/certificates/yok.pdf", CancellationToken.None));

        Assert.Equal(ErrorKeys.CertificateNotFound, exception.ErrorKey);
        Assert.Single((await _db.Context.Experts.FindAsync(expert.Id))!.Certificates);
        Assert.Empty(_storage.DeletedObjects);
    }

    [Fact]
    public async Task Matching_is_exact_so_a_case_variant_does_not_delete_anything()
    {
        var expert = await AddExpertAsync(cert => [cert("a")]);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeleteCertificateAsync(expert.Id, $"{expert.Id}/CERTIFICATES/a.pdf", CancellationToken.None));

        Assert.Equal(ErrorKeys.CertificateNotFound, exception.ErrorKey);
    }

    [Fact]
    public async Task Caller_without_expert_profile_gets_expert_not_found()
    {
        var client = await _db.AddProfileAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeleteCertificateAsync(client.Id, $"{client.Id}/certificates/a.pdf", CancellationToken.None));

        Assert.Equal(ErrorKeys.ExpertNotFound, exception.ErrorKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_certificate_value_is_a_validation_error(string? certificate)
    {
        var expert = await AddExpertAsync(cert => [cert("a")]);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeleteCertificateAsync(expert.Id, certificate, CancellationToken.None));

        Assert.Equal(ErrorKeys.ValidationFailed, exception.ErrorKey);
        Assert.Equal("certificate", exception.Field);
    }

    [Fact]
    public async Task Legacy_value_outside_the_experts_folder_leaves_storage_untouched()
    {
        var other = Guid.NewGuid();
        var foreign = $"{other}/certificates/x.pdf";
        var expert = await AddExpertAsync(_ => [foreign, "BDT Uygulayıcı Sertifikası"]);

        var remaining = await Service().DeleteCertificateAsync(expert.Id, foreign, CancellationToken.None);

        Assert.Equal(["BDT Uygulayıcı Sertifikası"], remaining);
        Assert.Empty(_storage.DeletedObjects);
    }

    [Fact]
    public async Task Storage_failure_does_not_undo_the_removal()
    {
        var expert = await AddExpertAsync(cert => [cert("a")]);
        _storage.DeleteFailure = new DomainException(ErrorKeys.StorageUploadFailed);

        var remaining = await Service().DeleteCertificateAsync(expert.Id, $"{expert.Id}/certificates/a.pdf", CancellationToken.None);

        Assert.Empty(remaining);
        Assert.Empty((await _db.Context.Experts.FindAsync(expert.Id))!.Certificates);
    }

    [Fact]
    public async Task Approved_expert_deletion_applies_directly_without_pending_revision()
    {
        var expert = await AddExpertAsync(cert => [cert("a")], ExpertStatuses.Approved);

        await Service().DeleteCertificateAsync(expert.Id, $"{expert.Id}/certificates/a.pdf", CancellationToken.None);

        var stored = (await _db.Context.Experts.FindAsync(expert.Id))!;
        Assert.Empty(stored.Certificates);
        Assert.Null(stored.PendingRevision);
        Assert.Equal(ExpertStatuses.Approved, stored.Status);
    }

    public void Dispose() => _db.Dispose();

    private UploadService Service() => new(_db.UnitOfWork, _storage);

    private async Task<Expert> AddExpertAsync(
        Func<Func<string, string>, List<string>> certificates,
        string status = ExpertStatuses.Pending)
    {
        var profile = await _db.AddProfileAsync(role: ProfileRoles.Expert);
        var expert = new Expert
        {
            Id = profile.Id,
            Title = "Psikolog",
            Status = status,
            Certificates = certificates(name => $"{profile.Id}/certificates/{name}.pdf"),
        };
        _db.Context.Experts.Add(expert);
        await _db.Context.SaveChangesAsync();
        return expert;
    }
}
