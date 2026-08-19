using System.ComponentModel.DataAnnotations;

namespace Wec.Core.Configuration;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PositiveTimeSpanAttribute : ValidationAttribute
{
    public PositiveTimeSpanAttribute()
        : base("The {0} duration must be greater than zero.")
    {
    }

    public override bool IsValid(object? value) => value is TimeSpan duration && duration > TimeSpan.Zero;
}
