namespace PsikoAl.Common.Exceptions;

// Meta: hata gövdesine eklenen makine-okur ek bilgi (ör. OFFER_FEE_CHANGED'de yeni ücret).
// İstemcinin ikinci bir istek atmadan karar verebilmesi için.
public sealed class DomainException(string errorKey, string? field = null, object? meta = null) : Exception(errorKey)
{
    public string ErrorKey { get; } = errorKey;

    public string? Field { get; } = field;

    public object? Meta { get; } = meta;
}
