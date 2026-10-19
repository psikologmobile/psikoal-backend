namespace PsikoAl.Common.Dtos.Wallet;

public sealed record WalletPaymentDto(
    Guid Id,
    long AmountKurus,
    string AmountLabel,
    string Status,
    string StatusLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    WalletInvoiceDto? Invoice);

public sealed record WalletInvoiceDto(string FileName, DateTimeOffset UploadedAt);

public sealed record InvoiceUrlDto(string Url);
