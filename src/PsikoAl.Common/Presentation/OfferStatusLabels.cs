using PsikoAl.Common.Constants;

namespace PsikoAl.Common.Presentation;

// REVIZE-2 §4: Gönderildi · Seçildi · Başka uzmanla eşleşti · İlan kapandı · Süresi doldu · Geri çekildi.
// closed_reason'sız REJECTED yalnız Revize 2 öncesinin elle reddedilmiş teklifleridir;
// onlar için en yakın nötr karşılık "İlan kapandı".
public static class OfferStatusLabels
{
    public static string For(string status, string? closedReason)
        => status switch
        {
            OfferStatuses.Pending => "Gönderildi",
            OfferStatuses.Accepted => "Seçildi",
            OfferStatuses.Withdrawn => "Geri çekildi",
            OfferStatuses.Rejected => closedReason switch
            {
                OfferClosedReasons.OtherExpert => "Başka uzmanla eşleşti",
                OfferClosedReasons.ListingExpired => "Süresi doldu",
                _ => "İlan kapandı",
            },
            _ => status,
        };
}
