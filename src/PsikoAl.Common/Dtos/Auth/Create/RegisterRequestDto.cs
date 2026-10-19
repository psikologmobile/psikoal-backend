namespace PsikoAl.Common.Dtos.Auth.Create;

// ReferralCode: davet eden uzmanın kodu (REVIZE-2 §7). Geçersizse sessizce yok sayılır.
public sealed record RegisterRequestDto(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Role,
    bool TermsAccepted,
    bool HealthDataConsent,
    string? ReferralCode = null);
