using FluentValidation;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Payment.Create;

namespace PsikoAl.Api.Validators;

public sealed class PayTrInitRequestDtoValidator : AbstractValidator<PayTrInitRequestDto>
{
    public PayTrInitRequestDtoValidator()
    {
        RuleFor(request => request.AmountKurus).GreaterThan(0).WithMessage(ErrorKeys.ValidationFailed);
    }
}
