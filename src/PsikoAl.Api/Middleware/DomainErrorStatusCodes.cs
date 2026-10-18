using PsikoAl.Common.Constants;

namespace PsikoAl.Api.Middleware;

// Gövde (ApiErrorDto) her durumda aynı; yalnız HTTP durum kodu hatanın türünü söyler.
// Listede olmayan her kod iş kuralı ihlalidir ve 422 kalır. AUTH_* kodları bilinçli
// olarak 422'de bırakıldı: giriş/kayıt ekranları ve seed betikleri o akışa bağlı.
public static class DomainErrorStatusCodes
{
    private static readonly HashSet<string> NotFound = new(StringComparer.Ordinal)
    {
        ErrorKeys.ProfileNotFound,
        ErrorKeys.ExpertNotFound,
        ErrorKeys.CategoryNotFound,
        ErrorKeys.ReviewNotFound,
        ErrorKeys.ListingNotFound,
        ErrorKeys.OfferNotFound,
        ErrorKeys.MatchNotFound,
        ErrorKeys.NotificationNotFound,
        ErrorKeys.NotificationTemplateNotFound,
        ErrorKeys.PushTokenNotFound,
        ErrorKeys.AssessmentNotFound,
        ErrorKeys.AssessmentResultNotFound,
        ErrorKeys.WalletPaymentNotFound,
        ErrorKeys.InvoiceNotFound,
        ErrorKeys.ClaimNotFound,
    };

    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        ErrorKeys.AdminUserNotFound,
        ErrorKeys.ExpertRoleRequired,
        ErrorKeys.ListingClientRoleRequired,
        ErrorKeys.OfferExpertRoleRequired,
        ErrorKeys.ReviewClientRoleRequired,
        ErrorKeys.MatchNotParticipant,
        ErrorKeys.ListingNotOwner,
        ErrorKeys.TopupExpertNotApproved,
    };

    // LISTING_NOT_OPEN 409: durum çakışması (REVIZE-2 §3.4) — ilan başka bir istekle
    // eşleşti/kapandı; istemci ilanı yenileyip yeniden karar verir.
    private static readonly HashSet<string> Conflict = new(StringComparer.Ordinal)
    {
        ErrorKeys.ExpertProfileAlreadyExists,
        ErrorKeys.CategorySlugAlreadyExists,
        ErrorKeys.ReviewAlreadyExists,
        ErrorKeys.OfferAlreadyExists,
        ErrorKeys.ResourceConflict,
        ErrorKeys.ListingNotOpen,
        ErrorKeys.OfferFeeChanged,
        ErrorKeys.ClaimAlreadyExists,
        ErrorKeys.ClaimAlreadyResolved,
    };

    public static int For(string errorKey)
    {
        if (NotFound.Contains(errorKey))
        {
            return StatusCodes.Status404NotFound;
        }

        if (Forbidden.Contains(errorKey))
        {
            return StatusCodes.Status403Forbidden;
        }

        return Conflict.Contains(errorKey)
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status422UnprocessableEntity;
    }
}
