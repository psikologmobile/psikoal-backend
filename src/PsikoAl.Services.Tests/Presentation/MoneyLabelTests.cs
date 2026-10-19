using PsikoAl.Common.Constants;
using PsikoAl.Common.Presentation;

namespace PsikoAl.Services.Tests.Presentation;

// Sözleşme: docs/REVIZE-2.md §0, §2.2, §4 — "₺1.250", "+₺500" / "−₺625" / "",
// "+1 hak" / "−1 hak" / "", "Ücretsiz hak". Eksi U+2212; kuruş gösterilmez.
public sealed class MoneyLabelTests
{
    [Theory]
    [InlineData(125_000, "₺1.250")]
    [InlineData(0, "₺0")]
    [InlineData(50_000, "₺500")]
    [InlineData(5_000_000, "₺50.000")]
    [InlineData(12_345, "₺123")]
    [InlineData(150, "₺2")]
    [InlineData(-62_500, "−₺625")]
    public void Try_formats_whole_lira_with_turkish_thousands(long kurus, string expected)
        => Assert.Equal(expected, MoneyLabel.Try(kurus));

    [Theory]
    [InlineData(50_000, "+₺500")]
    [InlineData(-62_500, "−₺625")]
    [InlineData(0, "")]
    public void WithSign_uses_plus_true_minus_or_nothing(long kurus, string expected)
        => Assert.Equal(expected, MoneyLabel.WithSign(kurus));

    [Theory]
    [InlineData(1, "+1 hak")]
    [InlineData(-1, "−1 hak")]
    [InlineData(3, "+3 hak")]
    [InlineData(0, "")]
    public void Rights_label(int delta, string expected)
        => Assert.Equal(expected, MoneyLabel.Rights(delta));

    [Fact]
    public void Minus_is_the_unicode_minus_sign_not_a_hyphen()
    {
        Assert.StartsWith("−", MoneyLabel.WithSign(-100), StringComparison.Ordinal);
        Assert.DoesNotContain("-", MoneyLabel.WithSign(-100), StringComparison.Ordinal);
    }

    [Fact]
    public void Fee_paid_label_names_the_free_right()
    {
        Assert.Equal("Ücretsiz hak", MoneyLabel.FeePaid(0, usedFreeRight: true));
        Assert.Equal("₺625", MoneyLabel.FeePaid(62_500, usedFreeRight: false));
    }

    [Theory]
    [InlineData(WalletTransactionTypes.Topup, "Bakiye yükleme")]
    [InlineData(WalletTransactionTypes.OfferFee, "Teklif ücreti")]
    [InlineData(WalletTransactionTypes.OfferFreeRight, "Ücretsiz hak kullanıldı")]
    [InlineData(WalletTransactionTypes.WelcomeRight, "Hoş geldin hakkı")]
    [InlineData(WalletTransactionTypes.ReferralRight, "Davet hakkı")]
    [InlineData(WalletTransactionTypes.AdminGift, "Hediye")]
    [InlineData(WalletTransactionTypes.AdminAdjust, "Düzeltme")]
    public void Wallet_type_labels_match_the_contract(string type, string expected)
        => Assert.Equal(expected, WalletLabels.TypeLabel(type));

    [Fact]
    public void Every_wallet_type_has_a_label()
        => Assert.All(WalletTransactionTypes.All, type => Assert.NotEqual(type, WalletLabels.TypeLabel(type)));

    [Theory]
    [InlineData(OfferStatuses.Pending, null, "Gönderildi")]
    [InlineData(OfferStatuses.Accepted, null, "Seçildi")]
    [InlineData(OfferStatuses.Rejected, OfferClosedReasons.OtherExpert, "Başka uzmanla eşleşti")]
    [InlineData(OfferStatuses.Rejected, OfferClosedReasons.ListingClosed, "İlan kapandı")]
    [InlineData(OfferStatuses.Rejected, OfferClosedReasons.ListingExpired, "Süresi doldu")]
    [InlineData(OfferStatuses.Rejected, null, "İlan kapandı")]
    [InlineData(OfferStatuses.Withdrawn, null, "Geri çekildi")]
    public void Offer_status_labels_match_the_contract(string status, string? closedReason, string expected)
        => Assert.Equal(expected, OfferStatusLabels.For(status, closedReason));

    [Fact]
    public void Ledger_descriptions_name_the_listing_and_the_reason()
    {
        Assert.Equal("\"Kaygı\" ilanına teklif ücreti", WalletLabels.Description(WalletTransactionTypes.OfferFee, "Kaygı", null));
        Assert.Equal("Düzeltme: hatalı düşüm", WalletLabels.Description(WalletTransactionTypes.AdminAdjust, null, "hatalı düşüm"));
        Assert.Equal("Hediye", WalletLabels.Description(WalletTransactionTypes.AdminGift, null, null));
    }
}
