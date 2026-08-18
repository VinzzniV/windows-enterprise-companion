using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

/// <summary>SQLite cannot order DateTimeOffset; store UTC ticks like the other WEC modules.</summary>
internal static class UtcTicks
{
    public static readonly ValueConverter<DateTimeOffset, long> Converter =
        new(timestamp => timestamp.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}
