using System.Data;
using System.Globalization;
using Dapper;

namespace DiarSpeicher.Infrastructure.Data;

public static class SqliteTypeHandlers
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());

        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.FileStatus>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.LibraryPattern>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.LibraryType>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.ReadingStatus>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.ReadingMode>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.ReadingDirection>();
        RegisterStringEnum<DiarSpeicher.Core.Domain.Enums.LibraryViewMode>();
    }

    private static void RegisterStringEnum<T>() where T : struct, Enum
    {
        SqlMapper.AddTypeMap(typeof(T), DbType.String);
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<T>());
        SqlMapper.AddTypeHandler(new NullableStringEnumTypeHandler<T>());
    }

    public sealed class StringEnumTypeHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
    {
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = value.ToString();
        }

        public override T Parse(object value) => value switch
        {
            T enumVal => enumVal,
            string s => Enum.Parse<T>(s, ignoreCase: true),
            _ => (T)Enum.ToObject(typeof(T), value)
        };
    }

    public sealed class NullableStringEnumTypeHandler<T> : SqlMapper.TypeHandler<T?> where T : struct, Enum
    {
        public override void SetValue(IDbDataParameter parameter, T? value)
        {
            parameter.Value = value.HasValue ? value.Value.ToString() : DBNull.Value;
        }

        public override T? Parse(object? value) => value switch
        {
            null or DBNull => null,
            T enumVal => enumVal,
            string s => string.IsNullOrWhiteSpace(s) ? null : Enum.Parse<T>(s, ignoreCase: true),
            _ => (T)Enum.ToObject(typeof(T), value)
        };
    }

    /// <summary>
    /// Acepta tambien marcas de tiempo completas: la columna guarda texto libre y hay filas
    /// escritas como instante, que <see cref="DateOnly.Parse(string, IFormatProvider)"/> rechaza.
    /// </summary>
    internal static DateOnly ParseDateOnly(string value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.FromDateTime(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime);

    public sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => ParseDateOnly(s),
            _ => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
        };
    }

    public sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.Value = value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DBNull.Value;
        }

        public override DateOnly? Parse(object? value) => value switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => string.IsNullOrWhiteSpace(s) ? null : ParseDateOnly(s),
            _ => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
        };
    }

    public sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value.ToString("o", CultureInfo.InvariantCulture);
        }

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }

    public sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
        {
            parameter.Value = value.HasValue
                ? value.Value.ToString("o", CultureInfo.InvariantCulture)
                : DBNull.Value;
        }

        public override DateTimeOffset? Parse(object? value) => value switch
        {
            null or DBNull => null,
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s => string.IsNullOrWhiteSpace(s) ? null : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }
}
