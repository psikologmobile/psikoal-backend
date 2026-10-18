namespace PsikoAl.Common.Constants;

public static class WalletFieldLimits
{
    public const int TransactionsDefaultPerPage = 20;
    public const int TransactionsMaxPerPage = 50;

    // Admin hediye/düzeltme üst sınırı: tek işlemde 100.000 TL ve 100 hak. Yanlışlıkla
    // fazladan sıfır yazılmış bir tutar (ör. kuruş yerine TL sanılması) kabul edilmesin.
    public const long AdminMaxAmountKurus = 10_000_000;
    public const int AdminMaxFreeRights = 100;
    public const int ReasonMaxLength = 500;
    public const int ClaimNoteMaxLength = 300;
    public const int InvoiceMaxBytes = 10 * 1024 * 1024;
}
