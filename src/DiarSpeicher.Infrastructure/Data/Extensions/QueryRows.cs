namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class QueryRows
{
    public static List<T> AsRowList<T>(this IEnumerable<T> rows) => rows as List<T> ?? [.. rows];
}
