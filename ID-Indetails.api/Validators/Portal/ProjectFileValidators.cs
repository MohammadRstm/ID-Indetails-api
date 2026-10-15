using FluentValidation;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;

namespace ID_Indetails.api.Validators.Portal;

public sealed class ProjectFileCommentRequestValidator : AbstractValidator<ProjectFileCommentRequest>
{
    public ProjectFileCommentRequestValidator()
    {
        RuleFor(x => x.Body).Written(ProjectFileRules.MaxCommentLength, "comment");
    }
}
