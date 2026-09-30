using FluentValidation;
using ID_Indetails.api.DTO.Request.Files;

namespace ID_Indetails.api.Validators.Files;

public sealed class AttachFileRequestValidator : AbstractValidator<AttachFileRequest>
{
    public AttachFileRequestValidator()
    {
        RuleFor(x => x.FileId).GreaterThan(0).WithMessage("Choose an uploaded photo.");
    }
}
