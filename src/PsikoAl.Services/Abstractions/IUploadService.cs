namespace PsikoAl.Services.Abstractions;

public interface IUploadService
{
    Task<string> UploadAvatarAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken);

    Task<string> UploadCvAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> UploadCertificateAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> DeleteCertificateAsync(Guid userId, string? certificate, CancellationToken cancellationToken);

    Task<string> UploadBlogCoverAsync(Guid blogId, Stream content, string contentType, CancellationToken cancellationToken);
}
