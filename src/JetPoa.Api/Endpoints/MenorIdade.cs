using System.Data;
using Dapper;

namespace JetPoa.Api.Endpoints;

/// <summary>
/// Menor de idade na abertura da comanda (check-in ou caixa).
/// Com data de nascimento no cadastro do cliente, ela decide. Sem data, a portaria pode informar a data
/// (fica gravada no cliente) ou apenas declarar que é menor.
/// </summary>
public static class MenorIdade
{
    public static bool EhMenor(DateTime nascimento, DateTime hoje) => nascimento.Date.AddYears(18) > hoje.Date;

    /// <returns>'S'/'N' para comanda.menor_idade, ou a mensagem de erro.</returns>
    public static async Task<(string? Flag, string? Erro)> DefinirAsync(
        IDbConnection c, IDbTransaction? tx, long clienteId, DateTime? nascimentoInformado, bool? declaradoMenor)
    {
        var nasc = await c.ExecuteScalarAsync<DateTime?>(
            "select data_nascimento from cliente where id = :clienteId", new { clienteId }, tx);
        if (nasc is null && nascimentoInformado is { } n)
        {
            if (n.Date > DateTime.Today || n.Date < DateTime.Today.AddYears(-120))
                return (null, "Data de nascimento inválida.");
            await c.ExecuteAsync("update cliente set data_nascimento = :n where id = :clienteId and data_nascimento is null",
                new { n = n.Date, clienteId }, tx);
            nasc = n.Date;
        }
        var menor = nasc is { } d ? EhMenor(d, DateTime.Today) : declaradoMenor == true;
        return (menor ? "S" : "N", null);
    }
}
