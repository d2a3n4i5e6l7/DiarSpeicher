using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DiarSpeicher.Infrastructure.Data.Conversions;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as UTC ticks. SQLite has no date type and refuses to
/// translate ORDER BY over a DateTimeOffset, which forced every "newest first" query to sort
/// in memory after materialising the table. As an integer the comparison translates to SQL,
/// so ordering and paging happen server-side.
///
/// Every timestamp in the domain is produced with UtcNow, so collapsing the offset to zero
/// loses nothing; UtcTicks would flatten a non-UTC offset to its UTC instant anyway, which is
/// the value that must be compared when ordering.
/// </summary>
public class DateTimeOffsetToTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    value => new DateTimeOffset(value, TimeSpan.Zero));

public class NullableDateTimeOffsetToTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    value => value.HasValue ? value.Value.UtcTicks : null,
    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
