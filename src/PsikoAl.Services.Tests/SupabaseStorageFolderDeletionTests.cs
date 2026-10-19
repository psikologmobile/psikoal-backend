using System.Net;
using System.Text;
using PsikoAl.Services;

namespace PsikoAl.Services.Tests;

// Hesap silmede documents/{userId}/ altındaki CV ve certificates/ alt klasörü de gitmeli.
// Storage LIST tek seviye döner; klasör id'siz satırdır ve ayrıca gezilmezse
// sertifikalar sessizce kalırdı.
public sealed class SupabaseStorageFolderDeletionTests
{
    [Fact]
    public async Task DeleteFolderAsync_walks_subfolders_and_deletes_every_object()
    {
        var handler = new RoutingHandler(new Dictionary<string, string>
        {
            ["u1"] = """[{"name":"cv.pdf","id":"a"},{"name":"certificates","id":null}]""",
            ["u1/certificates"] = """[{"name":"c1.pdf","id":"b"}]""",
        });
        var service = new SupabaseStorageService(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:54321/") });

        await service.DeleteFolderAsync("documents", "u1", CancellationToken.None);

        var delete = Assert.Single(handler.DeleteBodies);
        Assert.Contains("\"u1/cv.pdf\"", delete, StringComparison.Ordinal);
        Assert.Contains("\"u1/certificates/c1.pdf\"", delete, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteFolderAsync_on_an_empty_folder_sends_no_delete()
    {
        var handler = new RoutingHandler(new Dictionary<string, string> { ["u2"] = "[]" });
        var service = new SupabaseStorageService(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:54321/") });

        await service.DeleteFolderAsync("avatars", "u2", CancellationToken.None);

        Assert.Empty(handler.DeleteBodies);
    }

    // Sertifika silme: obje zaten yoksa (404) hedef durum sağlanmıştır, hata sayılmaz.
    [Fact]
    public async Task DeleteObjectAsync_treats_missing_object_as_done()
    {
        var service = new SupabaseStorageService(new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri("http://127.0.0.1:54321/") });

        await service.DeleteObjectAsync("documents", "u1/certificates/a.pdf", CancellationToken.None);
    }

    [Fact]
    public async Task DeleteObjectAsync_still_fails_on_server_errors()
    {
        var service = new SupabaseStorageService(new HttpClient(new StatusHandler(HttpStatusCode.InternalServerError)) { BaseAddress = new Uri("http://127.0.0.1:54321/") });

        await Assert.ThrowsAsync<PsikoAl.Common.Exceptions.DomainException>(
            () => service.DeleteObjectAsync("documents", "u1/certificates/a.pdf", CancellationToken.None));
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private sealed class RoutingHandler(Dictionary<string, string> listings) : HttpMessageHandler
    {
        public List<string> DeleteBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Method == HttpMethod.Delete)
            {
                DeleteBodies.Add(body);
                return Json("[]");
            }

            var prefix = listings.Keys.Single(key => body.Contains($"\"prefix\":\"{key}\"", StringComparison.Ordinal));
            return Json(listings[prefix]);
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
