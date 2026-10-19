using PsikoAl.Common.Constants;

namespace PsikoAl.Api.Middleware;

// Servisler "önce var mı bak, sonra ekle" yapıyor; iki eşzamanlı istek kontrolü birlikte
// geçerse Postgres benzersizlik kısıtı (23505) devreye girer. O ana kadar bu yarış boş
// bir 500 olarak dönüyordu — kısıt adı, kullanıcının anlayacağı domain koduna çevrilir.
public static class UniqueViolationErrorKeys
{
    private static readonly Dictionary<string, string> ByConstraint = new(StringComparer.Ordinal)
    {
        ["uq_offers_listing_expert"] = ErrorKeys.OfferAlreadyExists,
        ["uq_reviews_client_match"] = ErrorKeys.ReviewAlreadyExists,
        ["experts_pkey"] = ErrorKeys.ExpertProfileAlreadyExists,
        ["categories_slug_key"] = ErrorKeys.CategorySlugAlreadyExists,
        ["blogs_slug_key"] = ErrorKeys.BlogSlugAlreadyExists,
        ["uq_listing_claims_listing_expert"] = ErrorKeys.ClaimAlreadyExists,
    };

    public static string For(string? constraintName)
        => constraintName is not null && ByConstraint.TryGetValue(constraintName, out var errorKey)
            ? errorKey
            : ErrorKeys.ResourceConflict;
}
