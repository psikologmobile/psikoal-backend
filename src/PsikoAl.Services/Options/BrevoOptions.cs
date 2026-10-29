namespace PsikoAl.Services.Options;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string? ApiKey { get; set; }

    public string SenderEmail { get; set; } = "no-reply@psikoal.com";

    public string SenderName { get; set; } = "Psikosirius";
}
