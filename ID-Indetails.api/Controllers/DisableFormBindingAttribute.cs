using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Keeps MVC from reading a multipart body into a form before the action runs, so the action can read the upload as it
/// arrives (<see cref="FilesController.Upload"/>). Without it, the body is already used up when the action starts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
