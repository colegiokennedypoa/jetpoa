using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Data;

/// <summary>Abre conexões com o esquema BISTRO (Oracle XE 11.2).</summary>
public sealed class Db(IConfiguration config)
{
    private readonly string _connectionString =
        config.GetConnectionString("Bistro")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Bistro não configurada. Use a variável de ambiente ConnectionStrings__Bistro ou dotnet user-secrets.");

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>
    /// Paginação compatível com Oracle 11g (sem OFFSET/FETCH): envolve a consulta ordenada com ROWNUM.
    /// Usa os parâmetros :pg_ini e :pg_fim.
    /// </summary>
    public static string Paginar(string sqlOrdenado) =>
        $"select * from (select q.*, rownum rn__ from ({sqlOrdenado}) q where rownum <= :pg_fim) where rn__ > :pg_ini";
}
