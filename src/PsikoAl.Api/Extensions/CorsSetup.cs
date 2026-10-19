using Microsoft.AspNetCore.Cors.Infrastructure;

namespace PsikoAl.Api.Extensions;

// Web sitesi (Expo web derlemesi, ayrı alan adı) API'yi tarayıcıdan çağırır; CORS izni
// olmadan her istek tarayıcıda düşer. İzinli kökenler yalnız yapılandırmadan okunur:
// Cors:AllowedOrigins dizi ("Cors__AllowedOrigins__0") ya da virgülle ayrılmış tek değer
// ("Cors__AllowedOrigins=https://a,https://b") olabilir. Liste boşsa hiçbir köken izinli
// değildir (mobil uygulama ve PayTR callback'i Origin göndermediği için etkilenmez).
// Kimlik bilgisi (çerez) taşınmaz: oturum Bearer token'la gider, AllowCredentials YOK.
public static class CorsSetup
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Site";

    private static readonly TimeSpan PreflightMaxAge = TimeSpan.FromHours(1);

    public static IServiceCollection AddConfiguredCors(this IServiceCollection services)
    {
        services.AddCors();
        services
            .AddOptions<CorsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var origins = ReadAllowedOrigins(configuration);
                options.AddPolicy(PolicyName, policy =>
                {
                    policy
                        .WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .SetPreflightMaxAge(PreflightMaxAge);
                });
            });

        return services;
    }

    /// <summary>
    /// Cors:AllowedOrigins'i okur: dizi ya da virgül/noktalı virgülle ayrılmış değer.
    /// Sondaki "/" atılır (tarayıcı Origin başlığında yol göndermez). Joker ("*") ve
    /// mutlak http(s) adresi olmayan değerler başlangıçta hata verir — yanlış yazılmış
    /// bir değer sessizce "her yere açık" ya da "hiç açık değil" olmasın.
    /// </summary>
    public static string[] ReadAllowedOrigins(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName).GetSection("AllowedOrigins");
        var raw = new List<string>();
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            raw.Add(section.Value);
        }

        raw.AddRange(section.GetChildren().Select(child => child.Value).OfType<string>());

        var origins = raw
            .SelectMany(value => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var origin in origins)
        {
            var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && uri.AbsolutePath == "/"
                && string.IsNullOrEmpty(uri.Query);
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins geçersiz köken içeriyor: '{origin}'. Yalnız https://alan.adi biçimi kabul edilir (joker yok).");
            }
        }

        return origins;
    }
}
