namespace PsikoAl.Common.Constants;

// Admin "Anlaşma bildirimleri" listesindeki TÜRETİLMİŞ durum: tabloda kolon değildir.
//   RESOLVED       — admin çözüm notuyla kapattı
//   REVIEW_PENDING — CLAIM_ADMIN_REVIEW_DAYS geçti ve ilan hâlâ eşleşmedi
//   OPEN           — diğer her durum
public static class ClaimStatuses
{
    public const string Open = "OPEN";
    public const string ReviewPending = "REVIEW_PENDING";
    public const string Resolved = "RESOLVED";
}
