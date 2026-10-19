using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Options;

namespace PsikoAl.Services;

public sealed class SupabaseStorageService(HttpClient httpClient, IOptions<SupabaseOptions>? options = null)
    : ISupabaseStorageService
{
    private readonly string _publicBaseUrl = string.IsNullOrWhiteSpace(options?.Value.PublicUrl)
        ? httpClient.BaseAddress?.ToString() ?? string.Empty
        : options.Value.PublicUrl.TrimEnd('/') + "/";

    private const int ListPageSize = 1000;
    private const int DeleteBatchSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> UploadAsync(
        string bucket,
        string path,
        Stream content,
        string contentType,
        bool isPublicBucket,
        CancellationToken cancellationToken)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/v1/object/{bucket}/{path}")
        {
            Content = body,
        };
        request.Headers.Add("x-upsert", "true");

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(ErrorKeys.StorageUploadFailed);
        }

        return isPublicBucket ? GetPublicUrl(bucket, path) : path;
    }

    public string GetPublicUrl(string bucket, string path)
        => $"{_publicBaseUrl}storage/v1/object/public/{bucket}/{path}";

    public async Task<string> CreateSignedUrlAsync(
        string bucket,
        string path,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"storage/v1/object/sign/{bucket}/{path}",
            new { expiresIn = (int)validFor.TotalSeconds },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(ErrorKeys.StorageUploadFailed);
        }

        var signed = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(JsonOptions, cancellationToken)
            ?? throw new DomainException(ErrorKeys.StorageUploadFailed);
        // Eğik çizgi ELLE eklenir: Supabase'in döndürdüğü signedURL
        // "/object/sign/..." biçiminde ve TrimStart('/') baştaki çizgiyi siliyor.
        // Çizgi yeniden konmazsa sonuç "storage/v1object/sign/..." olur — sözdizimsel
        // olarak geçerli bir URL, ama 404. Belirti sessiz: uç 200 döner, imzalı
        // bağlantı üretilmiş GÖRÜNÜR ve hata ancak admin bağlantıya tıkladığında
        // ortaya çıkar.
        return $"{httpClient.BaseAddress}storage/v1/{signed.SignedUrl.TrimStart('/')}";
    }


    public async Task DeleteFolderAsync(string bucket, string folder, CancellationToken cancellationToken)
    {
        var paths = await ListObjectPathsAsync(bucket, folder.TrimEnd('/'), cancellationToken);
        foreach (var batch in paths.Chunk(DeleteBatchSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"storage/v1/object/{bucket}")
            {
                Content = JsonContent.Create(new { prefixes = batch }, options: JsonOptions),
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new DomainException(ErrorKeys.StorageUploadFailed);
            }
        }
    }

    public async Task DeleteObjectAsync(string bucket, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"storage/v1/object/{bucket}")
        {
            Content = JsonContent.Create(new { prefixes = new[] { path } }, options: JsonOptions),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);

        // Silinmek istenen obje zaten yoksa hedeflenen son durum sağlanmış demektir.
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new DomainException(ErrorKeys.StorageUploadFailed);
        }
    }

    // Storage LIST tek seviye döner: klasörler id'siz satır olarak gelir ve ayrıca gezilir
    // (documents/{userId}/certificates/... gibi).
    private async Task<List<string>> ListObjectPathsAsync(string bucket, string folder, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"storage/v1/object/list/{bucket}",
            new { prefix = folder, limit = ListPageSize, offset = 0 },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(ErrorKeys.StorageUploadFailed);
        }

        var entries = await response.Content.ReadFromJsonAsync<List<StorageEntry>>(JsonOptions, cancellationToken) ?? [];
        var paths = new List<string>();
        foreach (var entry in entries)
        {
            var path = $"{folder}/{entry.Name}";
            if (entry.Id is null)
            {
                paths.AddRange(await ListObjectPathsAsync(bucket, path, cancellationToken));
            }
            else
            {
                paths.Add(path);
            }
        }

        return paths;
    }
    private sealed record SignedUrlResponse([property: JsonPropertyName("signedURL")] string SignedUrl);

    private sealed record StorageEntry(string Name, string? Id);
}
