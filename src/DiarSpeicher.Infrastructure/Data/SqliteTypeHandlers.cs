using System.Data.Common;
using System.Globalization;
using Dapper;

namespace DiarSpeicher.Infrastructure.Data;

public static class SqliteTypeHandlers
{
    public sealed class StringEnumTypeHandler<T> : DbValueHandler<T> where T : struct, Enum
    {
        protected override void SetValueCore(DbParameter parameter, T value)
        {
            parameter.Value = value.ToString();
        }

        protected override T Parse(object? value) => value switch
        {
            T enumVal => enumVal,
            string s => Enum.Parse<T>(s, ignoreCase: true),
            _ => (T)Enum.ToObject(typeof(T), value ?? throw new ArgumentNullException(nameof(value)))
        };
    }

    /// <summary>
    /// Also accepts full timestamps: the column holds free text and some rows were written as
    /// an instant, which <see cref="DateOnly.Parse(string, IFormatProvider)"/> rejects.
    /// </summary>
    internal static DateOnly ParseDateOnly(string value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.FromDateTime(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime);

    public sealed class DateOnlyHandler : DbValueHandler<DateOnly>
    {
        protected override void SetValueCore(DbParameter parameter, DateOnly value)
        {
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        protected override DateOnly Parse(object? value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => ParseDateOnly(s),
            _ => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
        };
    }

    public sealed class DateTimeOffsetHandler : DbValueHandler<DateTimeOffset>
    {
        /// <summary>
        /// The value is handed over unformatted so Microsoft.Data.Sqlite writes it as "yyyy-MM-
        /// dd HH:mm:ss.FFFFFFFzzz": Dapper never applied this method to parameters, so that is
        /// the format of every existing row, and SQL sorts by that text. Writing "o" would mix
        /// separators ('T' versus a space) and the order would stop being chronological.
        /// </summary>
        protected override void SetValueCore(DbParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value;
        }

        protected override DateTimeOffset Parse(object? value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }
}
