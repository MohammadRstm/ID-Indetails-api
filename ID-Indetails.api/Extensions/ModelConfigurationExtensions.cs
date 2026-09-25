using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Extensions;

/// <summary>Small helpers that keep the entity configurations consistent.</summary>
public static class ModelConfigurationExtensions
{
    /// <summary>ISO 4217 currency code, stored as CHAR(3) (e.g. USD, LBP).</summary>
    public static PropertyBuilder<string> IsCurrencyCode(this PropertyBuilder<string> property)
        => property.IsRequired().HasMaxLength(3).IsFixedLength();

    /// <summary>Free text with no practical length limit (MySQL TEXT). For string properties.</summary>
    public static PropertyBuilder<TProperty> IsLongText<TProperty>(this PropertyBuilder<TProperty> property)
        => property.HasColumnType("text");

    /// <summary>A MySQL JSON document. For string properties.</summary>
    public static PropertyBuilder<TProperty> IsJson<TProperty>(this PropertyBuilder<TProperty> property)
        => property.HasColumnType("json");
}
