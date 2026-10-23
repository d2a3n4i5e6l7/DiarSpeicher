using System.Data;
using System.Globalization;
using Dapper;

namespace DiarSpeicher.Infrastructure.Data;

public static class SqliteTypeHandlers
{
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

    public sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        /// <summary>
        /// El valor se entrega sin formatear para que Microsoft.Data.Sqlite lo escriba como
        /// "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz": Dapper nunca aplico este metodo a parametros, asi que
        /// es el formato de todas las filas existentes, y el SQL ordena por ese texto. Escribir "o"
        /// mezclaria separadores ('T' frente a espacio) y el orden dejaria de ser cronologico.
        /// </summary>
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value;
        }

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }
}
