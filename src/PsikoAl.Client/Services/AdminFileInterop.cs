using Microsoft.JSInterop;

namespace PsikoAl.Client.Services;

/// <summary>wwwroot/js/admin-files.js modülünün sarmalayıcısı (devre başına bir kez yüklenir).</summary>
public sealed class AdminFileInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task DownloadTextAsync(string fileName, string content, string mimeType)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("downloadText", fileName, content, mimeType);
    }

    /// <summary>Yeni sekme açılamazsa (açılır pencere engeli) false döner.</summary>
    public async Task<bool> OpenInNewTabAsync(string url)
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("openInNewTab", url);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Devre kapanmışsa modül zaten tarayıcıyla birlikte gitti.
            }
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/admin-files.js");
}
