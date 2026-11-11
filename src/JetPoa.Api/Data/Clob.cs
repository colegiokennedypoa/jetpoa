using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Data;

/// <summary>
/// Parâmetro CLOB para Dapper + ODP.NET. Sem isso, textos acima de 4000 caracteres são enviados
/// com Size = -1 e o driver lança ArgumentOutOfRangeException.
/// </summary>
public sealed class Clob(string? valor) : SqlMapper.ICustomQueryParameter
{
    public void AddParameter(IDbCommand command, string name)
    {
        var p = (OracleParameter)command.CreateParameter();
        p.ParameterName = name;
        p.OracleDbType = OracleDbType.Clob;
        p.Value = (object?)valor ?? DBNull.Value;
        command.Parameters.Add(p);
    }

    public static implicit operator Clob(string? v) => new(v);
}
