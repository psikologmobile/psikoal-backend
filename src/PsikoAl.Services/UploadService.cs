using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Validation;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Services;

public sealed class UploadService(
    IUnitOfWork unitOfWork,
    ISupabaseStorageService storage) : IUploadService
{
    private static readonly string[] ImageContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] DocumentContentTypes = ["application/pdf", "image/jpeg", "image/png"];

    public async Task<string> UploadAvatarAsync(
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var validated = await ValidateAsync(content, contentType, ImageContentTypes, cancellationToken);
        var path = $"{userId}/avatar.{FileSignatures.ExtensionFor(contentType)}";
        var url = await storage.UploadAsync("avatars", path, validated, contentType, isPublicBucket: true, cancellationToken);

        profile.AvatarUrl = url;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return url;
    }

    public async Task<string> UploadCvAsync(
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        var validated = await ValidateAsync(content, contentType, DocumentContentTypes, cancellationToken);
        var path = $"{userId}/cv.{FileSignatures.ExtensionFor(contentType)}";
        var storedPath = await storage.UploadAsync("documents", path, validated, contentType, isPublicBucket: false, cancellationToken);

        expert.CvUrl = storedPath;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return storedPath;
    }

    public async Task<IReadOnlyList<string>> UploadCertificateAsync(
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        if (expert.Certificates.Count >= ExpertDocumentPaths.MaxCertificates)
        {
            throw new DomainException(ErrorKeys.ExpertCertificateLimitExceeded, "file");
        }

        var validated = await ValidateAsync(content, contentType, DocumentContentTypes, cancellationToken);
        var path = $"{userId}/certificates/{Guid.NewGuid()}.{FileSignatures.ExtensionFor(contentType)}";
        var storedPath = await storage.UploadAsync("documents", path, validated, contentType, isPublicBucket: false, cancellationToken);

        expert.Certificates = [.. expert.Certificates, storedPath];
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return expert.Certificates;
    }

    public async Task<IReadOnlyList<string>> DeleteCertificateAsync(
        Guid userId,
        string? certificate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(certificate))
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "certificate");
        }

        var expert = await unitOfWork.Experts.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        // İstemci değeri ExpertDto.certificates'ten aynen geri gönderir; birebir (ordinal)
        // eşleşme dışında bir yorum yapılmaz ki başka bir öğe yanlışlıkla silinmesin.
        if (!expert.Certificates.Contains(certificate, StringComparer.Ordinal))
        {
            throw new DomainException(ErrorKeys.CertificateNotFound, "certificate");
        }

        // Yükleme gibi silme de yayındaki profile doğrudan yazılır (PendingRevision'a düşmez):
        // belgeler revizyon kapsamında değil, admin onları ayrı uçtan imzalayıp görür.
        expert.Certificates = [.. expert.Certificates.Where(item => !string.Equals(item, certificate, StringComparison.Ordinal))];
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Storage silmesi service_role ile yapılıyor (RLS yok). Serbest metin döneminden kalan
        // bir değer başka uzmanın klasörünü gösterebilir; yalnız kendi klasöründeki obje silinir,
        // diğerleri yalnız listeden düşer.
        if (ExpertDocumentPaths.IsOwnedBy(userId, certificate))
        {
            await TryDeleteStorageAsync("documents", certificate, cancellationToken);
        }

        return expert.Certificates;
    }

    public async Task<string> UploadBlogCoverAsync(
        Guid blogId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateAsync(content, contentType, ImageContentTypes, cancellationToken);
        var path = $"{blogId}/cover-{Guid.NewGuid():N}.{FileSignatures.ExtensionFor(contentType)}";
        await storage.UploadAsync(StorageBuckets.BlogMedia, path, validated, contentType, isPublicBucket: true, cancellationToken);
        return path;
    }

    // Kayıt zaten kaydedildi; Storage silmesi başarısız olursa yalnız sahipsiz bir dosya
    // kalır (hesap silmede klasörle birlikte gider). Uzmanın işlemini bu yüzden düşürmek
    // yanlış olur — liste güncellendi ama istemci hata görürdü.
    private async Task TryDeleteStorageAsync(string bucket, string path, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteObjectAsync(bucket, path, cancellationToken);
        }
        catch (DomainException)
        {
        }
    }

    private static async Task<Stream> ValidateAsync(
        Stream content,
        string contentType,
        string[] allowedContentTypes,
        CancellationToken cancellationToken)
    {
        if (!allowedContentTypes.Contains(contentType) || !FileSignatures.IsSupported(contentType))
        {
            throw new DomainException(ErrorKeys.FileTypeNotAllowed, "file");
        }

        // Content-Type header'ına güvenilmez: gerçek dosya imzası (magic bytes) doğrulanır.
        var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        if (!FileSignatures.Matches(contentType, buffered.GetBuffer().AsSpan(0, (int)Math.Min(buffered.Length, 8))))
        {
            throw new DomainException(ErrorKeys.FileTypeNotAllowed, "file");
        }

        buffered.Position = 0;
        return buffered;
    }
}
