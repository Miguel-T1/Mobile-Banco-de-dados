using System.Globalization;
using System.Runtime.InteropServices;

namespace TdeMauiBanco.Infrastructure;

/// <summary>
/// Adaptador para a biblioteca SQLite nativa já distribuída pelo Windows.
/// </summary>
internal sealed class NativeSqliteConnection : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;

    private IntPtr _database;

    public NativeSqliteConnection(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Esta versão usa a biblioteca SQLite nativa do Windows.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("Caminho do banco inválido.", nameof(databasePath)));

        var result = NativeSqlite.sqlite3_open16(databasePath, out _database);
        if (result != SqliteOk)
        {
            var mensagem = GetErrorMessage();
            Dispose();
            throw new InvalidOperationException($"Não foi possível abrir o banco SQLite: {mensagem}");
        }

        Execute("PRAGMA foreign_keys = ON;");
        Execute("PRAGMA busy_timeout = 5000;");
    }

    public int Changes => NativeSqlite.sqlite3_changes(_database);

    public void Execute(string sql, params object?[] parametros)
    {
        using var statement = Prepare(sql);
        statement.Bind(parametros);

        var result = statement.Step();

        while (result == SqliteRow)
        {
            result = statement.Step();
        }

        if (result != SqliteDone)
        {
            ThrowForResult(result, "executar o comando");
        }
    }

    public long Insert(string sql, params object?[] parametros)
    {
        Execute(sql, parametros);
        return NativeSqlite.sqlite3_last_insert_rowid(_database);
    }

    public long ScalarLong(string sql, params object?[] parametros)
    {
        using var statement = Prepare(sql);
        statement.Bind(parametros);

        var result = statement.Step();
        return result switch
        {
            SqliteRow => statement.GetInt64(0),
            SqliteDone => 0,
            _ => ThrowScalar(result)
        };
    }

    public List<T> Query<T>(
        string sql,
        Func<NativeSqliteStatement, T> mapear,
        params object?[] parametros)
    {
        using var statement = Prepare(sql);
        statement.Bind(parametros);

        var resultado = new List<T>();
        while (true)
        {
            var result = statement.Step();
            if (result == SqliteDone)
            {
                return resultado;
            }

            if (result != SqliteRow)
            {
                ThrowForResult(result, "ler dados");
            }

            resultado.Add(mapear(statement));
        }
    }

    public void BeginTransaction() => Execute("BEGIN IMMEDIATE;");

    public void Commit() => Execute("COMMIT;");

    public void Rollback()
    {
        try
        {
            Execute("ROLLBACK;");
        }
        catch
        {
            // A exceção original é mais útil do que uma falha secundária no rollback.
        }
    }

    public void Dispose()
    {
        if (_database == IntPtr.Zero)
        {
            return;
        }

        NativeSqlite.sqlite3_close_v2(_database);
        _database = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private NativeSqliteStatement Prepare(string sql)
    {
        var result = NativeSqlite.sqlite3_prepare16_v2(
            _database,
            sql,
            -1,
            out var statement,
            IntPtr.Zero);

        if (result != SqliteOk)
        {
            ThrowForResult(result, "preparar o comando");
        }

        return new NativeSqliteStatement(this, statement);
    }

    private long ThrowScalar(int result)
    {
        ThrowForResult(result, "ler o valor");
        return 0;
    }

    internal void ThrowForResult(int result, string operation)
    {
        throw new InvalidOperationException(
            $"Falha ao {operation} no SQLite ({result}): {GetErrorMessage()}");
    }

    private string GetErrorMessage()
    {
        if (_database == IntPtr.Zero)
        {
            return "erro desconhecido";
        }

        var pointer = NativeSqlite.sqlite3_errmsg16(_database);
        return Marshal.PtrToStringUni(pointer) ?? "erro desconhecido";
    }
}

internal sealed class NativeSqliteStatement : IDisposable
{
    private static readonly IntPtr SqliteTransient = new(-1);

    private readonly NativeSqliteConnection _connection;
    private IntPtr _statement;

    internal NativeSqliteStatement(NativeSqliteConnection connection, IntPtr statement)
    {
        _connection = connection;
        _statement = statement;
    }

    public void Bind(IReadOnlyList<object?> parametros)
    {
        var esperados = NativeSqlite.sqlite3_bind_parameter_count(_statement);
        if (esperados != parametros.Count)
        {
            throw new ArgumentException(
                $"O comando SQLite espera {esperados} parâmetros, mas recebeu {parametros.Count}.");
        }

        for (var indice = 0; indice < parametros.Count; indice++)
        {
            BindValue(indice + 1, parametros[indice]);
        }
    }

    public int Step() => NativeSqlite.sqlite3_step(_statement);

    public long GetInt64(int ordinal) =>
        NativeSqlite.sqlite3_column_int64(_statement, ordinal);

    public int GetInt32(int ordinal) =>
        checked((int)NativeSqlite.sqlite3_column_int64(_statement, ordinal));

    public double GetDouble(int ordinal) =>
        NativeSqlite.sqlite3_column_double(_statement, ordinal);

    public string GetText(int ordinal)
    {
        var pointer = NativeSqlite.sqlite3_column_text16(_statement, ordinal);
        return pointer == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringUni(pointer) ?? string.Empty;
    }

    public void Dispose()
    {
        if (_statement == IntPtr.Zero)
        {
            return;
        }

        NativeSqlite.sqlite3_finalize(_statement);
        _statement = IntPtr.Zero;
    }

    private void BindValue(int indice, object? valor)
    {
        var result = valor switch
        {
            null => NativeSqlite.sqlite3_bind_null(_statement, indice),
            string texto => NativeSqlite.sqlite3_bind_text16(
                _statement, indice, texto, -1, SqliteTransient),
            DateTime data => NativeSqlite.sqlite3_bind_text16(
                _statement,
                indice,
                data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                -1,
                SqliteTransient),
            DateOnly data => NativeSqlite.sqlite3_bind_text16(
                _statement,
                indice,
                data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                -1,
                SqliteTransient),
            decimal numero => NativeSqlite.sqlite3_bind_double(
                _statement, indice, decimal.ToDouble(numero)),
            double numero => NativeSqlite.sqlite3_bind_double(_statement, indice, numero),
            float numero => NativeSqlite.sqlite3_bind_double(_statement, indice, numero),
            long numero => NativeSqlite.sqlite3_bind_int64(_statement, indice, numero),
            int numero => NativeSqlite.sqlite3_bind_int64(_statement, indice, numero),
            short numero => NativeSqlite.sqlite3_bind_int64(_statement, indice, numero),
            bool valorBooleano => NativeSqlite.sqlite3_bind_int64(
                _statement, indice, valorBooleano ? 1 : 0),
            _ => throw new ArgumentException(
                $"Tipo de parâmetro SQLite não suportado: {valor.GetType().Name}.")
        };

        if (result != 0)
        {
            _connection.ThrowForResult(result, "atribuir um parâmetro");
        }
    }
}

internal static class NativeSqlite
{
    private const string Library = "winsqlite3.dll";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode,
        EntryPoint = "sqlite3_open16")]
    internal static extern int sqlite3_open16(string filename, out IntPtr database);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_close_v2(IntPtr database);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode,
        EntryPoint = "sqlite3_prepare16_v2")]
    internal static extern int sqlite3_prepare16_v2(
        IntPtr database,
        string sql,
        int bytes,
        out IntPtr statement,
        IntPtr tail);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_step(IntPtr statement);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_finalize(IntPtr statement);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_bind_parameter_count(IntPtr statement);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_bind_null(IntPtr statement, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_bind_double(IntPtr statement, int index, double value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode,
        EntryPoint = "sqlite3_bind_text16")]
    internal static extern int sqlite3_bind_text16(
        IntPtr statement,
        int index,
        [MarshalAs(UnmanagedType.LPWStr)] string value,
        int bytes,
        IntPtr destructor);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long sqlite3_column_int64(IntPtr statement, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double sqlite3_column_double(IntPtr statement, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sqlite3_column_text16")]
    internal static extern IntPtr sqlite3_column_text16(IntPtr statement, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sqlite3_errmsg16")]
    internal static extern IntPtr sqlite3_errmsg16(IntPtr database);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long sqlite3_last_insert_rowid(IntPtr database);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sqlite3_changes(IntPtr database);
}
