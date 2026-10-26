using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Binds an enum from the query string or route by the snake_case name the JSON uses (<c>payment_voided</c>), as well
/// as by its own name. MVC's own binder only knows <c>PaymentVoided</c>, so multi-word values in a query would fail.
/// </summary>
public sealed class SnakeCaseEnumBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => context.Metadata.UnderlyingOrModelType.IsEnum ? new SnakeCaseEnumBinder() : null;
}

public sealed class SnakeCaseEnumBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var text = result.FirstValue;
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        var type = bindingContext.ModelMetadata.UnderlyingOrModelType;
        if (Enum.TryParse(type, text.Replace("_", ""), ignoreCase: true, out var value) && Enum.IsDefined(type, value!))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"The value '{text}' is not valid for {bindingContext.ModelMetadata.Name}.");
        }

        return Task.CompletedTask;
    }
}
