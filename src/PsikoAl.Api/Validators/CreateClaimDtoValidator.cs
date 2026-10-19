using FluentValidation;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Claim;

namespace PsikoAl.Api.Validators;

public sealed class CreateClaimDtoValidator : AbstractValidator<CreateClaimDto>
{
    public CreateClaimDtoValidator()
    {
        RuleFor(request => request.Note).MaximumLength(WalletFieldLimits.ClaimNoteMaxLength).WithMessage(ErrorKeys.ValidationFailed);
    }
}
