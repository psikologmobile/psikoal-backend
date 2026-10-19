namespace PsikoAl.Common.Dtos.Wallet;

public sealed record WalletPageResult<T>(IReadOnlyList<T> Data, int Page, int PerPage, int Total);
