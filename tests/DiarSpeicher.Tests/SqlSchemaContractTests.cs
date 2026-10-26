using System.Text.RegularExpressions;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// Prepara contra el esquema real cada consulta escrita a mano en el codigo.
/// <para>
/// Al salir de EF los SELECT dejaron de generarse desde el modelo y pasaron a ser cadenas:
/// un nombre de columna equivocado ya no lo ve el compilador y solo revienta cuando esa ruta
/// se ejecuta. Preparar la sentencia obliga a SQLite a resolver cada nombre sin necesidad de
/// datos ni de recorrer el endpoint.
/// </para>
/// </summary>
public sealed class SqlSchemaContractTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly ITestOutputHelper _output;

    public SqlSchemaContractTests(ITestOutputHelper output)
    {
        _output = output;
        _db = new TestSqliteDatabase();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("No se encontro la raiz del repositorio");
    }

    /// <summary>
    /// Ficheros que hablan con bases auxiliares propias —el catalogo descargado de MangaBaka y
    /// el indice de carpetas—, con esquema y convencion de nombres aparte del catalogo.
    /// </summary>
    private static readonly string[] OwnDatabaseClasses = ["MangaBakaCatalog", "FolderIndex"];

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
                 .Where(f => !OwnDatabaseClasses.Any(c => Path.GetFileName(f).StartsWith(c + ".", StringComparison.Ordinal)));

    /// <summary>
    /// Toma el cuerpo de cada literal crudo (""" ... """) que contenga una sentencia SQL.
    /// </summary>
    private static IEnumerable<(string File, string Sql)> ExtractRawSqlLiterals(string path)
    {
        var text = File.ReadAllText(path);
        foreach (Match m in Regex.Matches(text, "\"\"\"(.*?)\"\"\"", RegexOptions.Singleline))
        {
            var body = m.Groups[1].Value;
            if (Regex.IsMatch(body, @"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM)\b", RegexOptions.IgnoreCase))
            {
                yield return (Path.GetFileName(path), body);
            }
        }
    }

    /// <summary>
    /// Dapper expande <c>IN @x</c> y los parametros con nombre; SQLite no entiende ninguna de
    /// las dos formas, asi que se sustituyen por algo que si pueda preparar.
    /// </summary>
    private static string MakePreparable(string sql)
    {
        // Los huecos de interpolacion llevan una condicion ya construida, nunca un nombre.
        sql = Regex.Replace(sql, @"\{[^{}]*\}", "1=1");
        sql = Regex.Replace(sql, @"IN\s+@\w+", "IN (NULL)", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"@\w+", "NULL");
        return sql;
    }

    /// <summary>
    /// Deja fuera lo que no es una sentencia entera: trozos que se concatenan para formar una
    /// consulta mas grande, y aquellas cuyo nombre de tabla llega por interpolacion —el hueco
    /// se sustituye por una condicion, que en esa posicion no es SQL valido—.
    /// </summary>
    private static IEnumerable<string> SplitStatements(string sql) =>
        sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(s => Regex.IsMatch(s, @"^\s*(SELECT|INSERT|UPDATE|DELETE|WITH)\b", RegexOptions.IgnoreCase))
           .Where(s => !Regex.IsMatch(s, @"(FROM|INTO|UPDATE|JOIN)\s+1=1", RegexOptions.IgnoreCase))
           .Where(s => !Regex.IsMatch(s, @"^\s*SELECT\b", RegexOptions.IgnoreCase)
                       || Regex.IsMatch(s, @"\bFROM\b", RegexOptions.IgnoreCase));

    [Fact]
    public void CadaConsultaEscritaAManoResuelveContraElEsquema()
    {
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var file in SourceFiles())
        {
            foreach (var (name, raw) in ExtractRawSqlLiterals(file))
            {
                foreach (var statement in SplitStatements(MakePreparable(raw)))
                {
                    checkedCount++;
                    using var cmd = _db.Connection.CreateCommand();
                    cmd.CommandText = statement;
                    try
                    {
                        cmd.Prepare();
                    }
                    catch (SqliteException ex)
                    {
                        failures.Add($"{name}: {ex.Message}\n    {statement.ReplaceLineEndings(" ")[..Math.Min(160, statement.Length)]}");
                    }
                }
            }
        }

        _output.WriteLine($"Sentencias preparadas: {checkedCount}");
        Assert.True(checkedCount > 50, $"Se esperaban muchas sentencias, se hallaron {checkedCount}");
        Assert.True(failures.Count == 0, $"{failures.Count} consultas no resuelven:\n" + string.Join("\n", failures));
    }
}
