namespace PsikoAl.Common.Dtos.Listing.Create;

// ContactConsent: danışanın ad soyad, telefon ve e-postasının teklif veren uzmanlarla
// paylaşılmasına AÇIK rızası (REVIZE-2 §3.2). Alan gönderilmezse false → 422.
// Phone: profilde telefon yoksa zorunlu; gelirse profile yazılır.
public sealed record CreateListingDto(
    string Title,
    string? Description,
    IReadOnlyList<string> Specialization,
    decimal BudgetMin,
    decimal BudgetMax,
    string PreferredSessionType,
    string? City,
    Guid? AssessmentResultId,
    bool ContactConsent = false,
    string? Phone = null);
