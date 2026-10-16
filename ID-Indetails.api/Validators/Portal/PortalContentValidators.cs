using FluentValidation;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;

namespace ID_Indetails.api.Validators.Portal;

public sealed class StartPortalUploadRequestValidator : AbstractValidator<StartPortalUploadRequest>
{
    public StartPortalUploadRequestValidator()
    {
        RuleFor(x => x.ClientToken)
            .NotEmpty().WithMessage("The upload token is missing.")
            .MaximumLength(64).WithMessage("The upload token is invalid.");

        RuleFor(x => x.Section)
            .NotNull().WithMessage("Choose a section.")
            .IsInEnum().WithMessage("Choose Presentation, 3D or Drawings.")
            .When(x => !x.Hero);
        RuleFor(x => x.Section).Null().WithMessage("A hero image belongs to no section.").When(x => x.Hero);

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("The file has no name.")
            .MaximumLength(1000).WithMessage("The file name is too long.");

        RuleFor(x => x.Size)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(PortalContentRules.MaxFileBytes).WithMessage("Files can be at most 1 GB.");

        RuleFor(x => x.ReplaceItemId).GreaterThan(0).When(x => x.ReplaceItemId is not null);
        RuleFor(x => x.ReplaceItemId).Null().WithMessage("A hero image replaces no file.").When(x => x.Hero);
    }
}

public sealed class PortalOrderRequestValidator : AbstractValidator<PortalOrderRequest>
{
    public PortalOrderRequestValidator()
    {
        RuleFor(x => x.ItemIds).NotEmpty().WithMessage("List the section's files in their new order.");
        RuleForEach(x => x.ItemIds).GreaterThan(0);
    }
}

public sealed class PortalHeroRequestValidator : AbstractValidator<PortalHeroRequest>
{
    public PortalHeroRequestValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0).WithMessage("Choose an image.");
    }
}

public sealed class PortalHeroOrderRequestValidator : AbstractValidator<PortalHeroOrderRequest>
{
    public PortalHeroOrderRequestValidator()
    {
        RuleFor(x => x.FileIds).NotEmpty().WithMessage("List the hero images in their new order.");
        RuleForEach(x => x.FileIds).GreaterThan(0);
    }
}

public sealed class PortalSummaryRequestValidator : AbstractValidator<PortalSummaryRequest>
{
    public PortalSummaryRequestValidator()
    {
        RuleFor(x => x.Summary!)
            .MaximumLength(PortalPublishRules.MaxSummaryLength)
            .WithMessage($"The introduction can be at most {PortalPublishRules.MaxSummaryLength} characters.")
            .When(x => x.Summary is not null);
    }
}

public sealed class PortalRevisionRequestValidator : AbstractValidator<PortalRevisionRequest>
{
    public PortalRevisionRequestValidator()
    {
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed class PortalPartLinksRequestValidator : AbstractValidator<PortalPartLinksRequest>
{
    public PortalPartLinksRequestValidator()
    {
        RuleFor(x => x.PartNumbers)
            .NotEmpty().WithMessage("Ask for at least one part.")
            .Must(p => p.Count <= 100).WithMessage("Ask for at most 100 parts at a time.");

        RuleForEach(x => x.PartNumbers)
            .InclusiveBetween(1, PortalContentRules.MaxParts).WithMessage("That part number doesn't exist.");
    }
}

public sealed class UpdatePortalItemRequestValidator : AbstractValidator<UpdatePortalItemRequest>
{
    public UpdatePortalItemRequestValidator()
    {
        RuleFor(x => x).Must(x => x.Title is not null || x.Section is not null)
            .WithMessage("Give a new title or a section.");

        RuleFor(x => x.Title!)
            .Must(t => t.Trim().Length > 0).WithMessage("Enter a title.")
            .MaximumLength(PortalContentRules.MaxTitleLength).WithMessage($"Titles can be at most {PortalContentRules.MaxTitleLength} characters.")
            .When(x => x.Title is not null);

        RuleFor(x => x.Section).IsInEnum().WithMessage("Choose Presentation, 3D or Drawings.").When(x => x.Section is not null);
    }
}
