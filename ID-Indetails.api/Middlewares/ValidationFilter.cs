using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Runs the registered FluentValidation validator for every action argument before the
/// action executes. Failures throw <see cref="ValidationException"/>, which the global
/// exception handler turns into the uniform 400 response, so controllers never validate by hand.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        await next();
    }
}
