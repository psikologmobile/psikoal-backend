using Microsoft.AspNetCore.DataProtection;
using PsikoAl.Client.Components;
using PsikoAl.Client.Security;
using PsikoAl.Client.Services;
using ShadCn.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddShadCnBlazorComponents();

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PsikoAl.Admin");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

var apiBaseAddress = ResolveApiBaseAddress(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AdminSessionState>();
builder.Services.AddScoped<AdminSessionStore>();
builder.Services.AddScoped<AdminFileInterop>();
builder.Services.AddHttpClient<IAdminAuthService, AdminAuthService>(client => client.BaseAddress = apiBaseAddress);
builder.Services.AddHttpClient<IAdminClientService, AdminClientService>(client => client.BaseAddress = apiBaseAddress);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);

app.Run();

static Uri ResolveApiBaseAddress(IConfiguration configuration)
{
    var apiBaseUrl = configuration["Api:BaseUrl"];
    if (string.IsNullOrWhiteSpace(apiBaseUrl))
    {
        throw new InvalidOperationException(
            "Api:BaseUrl yapılandırılmamış. Ortam değişkeni Api__BaseUrl ile API adresini verin.");
    }

    return new Uri(apiBaseUrl.TrimEnd('/') + "/");
}
