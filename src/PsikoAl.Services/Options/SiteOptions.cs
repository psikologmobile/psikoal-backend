namespace PsikoAl.Services.Options;

// Web sitesinin kök adresi (Site__PublicUrl), ör. https://psikoal.com. Davet linki
// `${PublicUrl}/davet/${kod}` buradan üretilir. Boşsa link göreli döner ("/davet/KOD").
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    public string? PublicUrl { get; set; }

    public string ReferralLink(string code)
        => $"{(PublicUrl ?? string.Empty).TrimEnd('/')}/davet/{code}";
}
