using FluentValidation;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Offer.Create;

namespace PsikoAl.Api.Validators;

public sealed class CreateOfferDtoValidator : AbstractValidator<CreateOfferDto>
{
    public CreateOfferDtoValidator()
    {
        RuleFor(request => request.ListingId).NotEmpty().WithMessage(ErrorKeys.ValidationFailed);
        RuleFor(request => request.Title).MaximumLength(100).WithMessage(ErrorKeys.ValidationFailed);
        RuleFor(request => request.Price).GreaterThan(0).WithMessage(ErrorKeys.ValidationFailed);
        RuleFor(request => request.Description).MaximumLength(300).WithMessage(ErrorKeys.ValidationFailed);
        RuleFor(request => request.ExpectedFeeKurus).GreaterThanOrEqualTo(0).WithMessage(ErrorKeys.ValidationFailed);
        RuleFor(request => request.SessionType)
            .Must(sessionType => sessionType is "online" or "yüz_yüze" or "yüz_yüze_online")
            .WithMessage(ErrorKeys.ValidationFailed);
    }
}
