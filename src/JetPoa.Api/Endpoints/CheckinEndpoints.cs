using System.Security.Claims;
using JetPoa.Api.Auth;
using System.Data;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Endpoints;

/// <summary>Entrada TERRA / RIO: abre a comanda vinculada ao cliente e lança as taxas do evento do dia.</summary>
public static class CheckinEndpoints
{
    public sealed class Evento
    {
        public long Id { get; set; }
        public DateTime Data { get; set; }
        public string Nome { get; set; } = "";
        public long? ProdutoTerraId { get; set; }
        public string? ProdutoTerra { get; set; }
        public decimal? ValorTerra { get; set; }
        public long? ProdutoRioId { get; set; }
        public string? ProdutoRio { get; set; }
        public decimal? ValorRio { get; set; }
        public long? CouvertProdutoId { get; set; }
        public string? CouvertProduto { get; set; }
        public decimal? ValorCouvert { get; set; }
        public DateTime? CouvertInicio { get; set; }
        public DateTime? CouvertFim { get; set; }
        public string Ativo { get; set; } = "S";
    }

    public sealed record EventoInput(DateTime Data, string Nome, long? ProdutoTerraId, long? ProdutoRioId,
        long? CouvertProdutoId, DateTime? CouvertInicio, DateTime? CouvertFim, bool Ativo = true);

    public sealed record NovoCliente(string Nome, string? Cpf, string? Celular, string? Email, string? Sexo, bool AceitaComunicacao);

    public sealed record CheckinInput(int Numero, string EntradaTipo, long? AreaId, long? ClienteId, NovoCliente? NovoCliente,
        string? Identificacao, string? Observacao, DateTime? DataNascimento = null, bool? MenorIdade = null);

    public sealed record CheckoutInput(int Numero);

    private sealed class Taxa
    {
        public long ProdutoId { get; set; }
        public string Nome { get; set; } = "";
        public decimal PrecoVenda { get; set; }
    }

    private const string SelectEvento = """
        select e.id, e.data, e.nome, e.produto_terra_id, pt.nome produto_terra, pt.preco_venda valor_terra,
               e.produto_rio_id, pr.nome produto_rio, pr.preco_venda valor_rio,
               e.couvert_produto_id, pc.nome couvert_produto, pc.preco_venda valor_couvert,
               e.couvert_inicio, e.couvert_fim, e.ativo
          from evento e
          left join produto pt on pt.id = e.produto_terra_id
          left join produto pr on pr.id = e.produto_rio_id
          left join produto pc on pc.id = e.couvert_produto_id
        """;

    public static void MapCheckin(this IEndpointRouteBuilder app)
    {
        var ev = app.MapGroup("/api/eventos").WithTags("Check-in / Eventos").RequireAuthorization(Permissoes.Checkin);
        var evEditar = ev.MapGroup("").RequireAuthorization(Permissoes.EventoEditar);

        ev.MapGet("", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Evento>(SelectEvento + " where e.data >= trunc(sysdate) - 30 order by e.data desc");
        });

        ev.MapGet("/hoje", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var e = await c.QuerySingleOrDefaultAsync<Evento>(SelectEvento + " where e.data = trunc(sysdate) and e.ativo = 'S'");
            return Results.Ok(e);
        });

        evEditar.MapPost("", async (Db db, EventoInput i, CancellationToken ct) => await SalvarEventoAsync(db, null, i, ct));
        evEditar.MapPut("/{id:long}", async (Db db, long id, EventoInput i, CancellationToken ct) => await SalvarEventoAsync(db, id, i, ct));

        var g = app.MapGroup("/api").WithTags("Check-in / Eventos").RequireAuthorization(Permissoes.Checkin);

        g.MapPost("/checkin", async (ClaimsPrincipal user, Db db, CheckinInput i, CancellationToken ct) =>
        {
            if (i.Numero <= 0) return Erro("Informe o número da comanda/pulseira.");
            var entrada = (i.EntradaTipo ?? "").Trim().ToUpperInvariant() switch { "T" or "TERRA" => "T", "R" or "RIO" => "R", _ => null };
            if (entrada is null) return Erro("Informe a entrada: Terra ou Rio.");

            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();

            // cliente: existente, novo (com deduplicação por CPF/celular) ou só um nome
            long? clienteId = i.ClienteId;
            string? nomeCliente = null;
            if (clienteId is null && i.NovoCliente is not null)
            {
                var (id, erro) = await ObterOuCriarClienteAsync(c, i.NovoCliente, tx);
                if (erro is not null) return Erro(erro);
                clienteId = id;
            }
            if (clienteId is not null)
                nomeCliente = await c.ExecuteScalarAsync<string?>("select nome from cliente where id = :clienteId", new { clienteId }, tx);
            var identificacao = string.IsNullOrWhiteSpace(i.Identificacao) ? null : i.Identificacao.Trim();
            // regra do JetPOA: comanda só para cliente cadastrado
            if (clienteId is null || nomeCliente is null) return Erro("Selecione o cliente ou cadastre-o (nome + CPF ou celular).");
            var (menor, erroIdade) = await MenorIdade.DefinirAsync(c, tx, clienteId.Value, i.DataNascimento, i.MenorIdade);
            if (erroIdade is not null) return Erro(erroIdade);

            var evento = await c.QuerySingleOrDefaultAsync<Evento>(SelectEvento + " where e.data = trunc(sysdate) and e.ativo = 'S'", transaction: tx);

            var comandaId = await c.ExecuteScalarAsync<long>("select sq_comanda.nextval from dual", transaction: tx);
            try
            {
                await c.ExecuteAsync("""
                    insert into comanda (id, numero, cliente_id, identificacao, entrada_tipo, area_id, observacao, evento_id, aberta_por, menor_idade)
                    values (:comandaId, :numero, :clienteId, :identificacao, :entrada, :areaId, :obs, :eventoId, :usr, :menor)
                    """, new { comandaId, usr = user.Id(), numero = i.Numero, clienteId, identificacao, entrada, areaId = i.AreaId, menor,
                              obs = string.IsNullOrWhiteSpace(i.Observacao) ? null : i.Observacao.Trim(), eventoId = evento?.Id }, tx);
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = $"A comanda {i.Numero} já está aberta. Use outra pulseira ou faça o check-out." });
            }

            // taxas do evento: entrada (terra/rio) e couvert se ainda dentro da janela
            var taxas = new List<Taxa>();
            if (evento is not null)
            {
                var entradaProd = entrada == "T" ? evento.ProdutoTerraId : evento.ProdutoRioId;
                if (entradaProd is not null) taxas.Add(await ProdutoAsync(c, entradaProd.Value, tx));
                if (evento.CouvertProdutoId is not null && evento.CouvertFim is not null && DateTime.Now <= evento.CouvertFim)
                    taxas.Add(await ProdutoAsync(c, evento.CouvertProdutoId.Value, tx));
            }
            foreach (var t in taxas)
                await c.ExecuteAsync("""
                    insert into comanda_item (comanda_id, produto_id, quantidade, preco_unitario, origem, lancado_por)
                    values (:comandaId, :ProdutoId, 1, :PrecoVenda, 'CHECKIN', :usr)
                    """, new { comandaId, t.ProdutoId, t.PrecoVenda, usr = user.Id() }, tx);

            tx.Commit();
            return Results.Ok(new
            {
                comandaId, numero = i.Numero, entrada, cliente = nomeCliente ?? identificacao, evento = evento?.Nome,
                menorIdade = menor == "S",
                taxas = taxas.Select(t => new { t.Nome, valor = t.PrecoVenda })
            });
        });

        // Check-out: registra a saída. Comanda aberta SEM nenhum item é encerrada aqui mesmo (não precisa ir ao caixa);
        // com consumo (ou taxas do evento) continua bloqueada e é encaminhada ao caixa.
        g.MapPost("/checkout", [Authorize(Permissoes.Checkout)] async (ClaimsPrincipal user, Db db, CheckoutInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var com = await c.QueryFirstOrDefaultAsync<(decimal Id, string Situacao, decimal? Total, string? Nome)>("""
                select co.id, co.situacao, co.valor_total, nvl(cl.nome, co.identificacao)
                  from comanda co left join cliente cl on cl.id = co.cliente_id
                 where co.numero = :numero and co.saida_em is null
                   and (co.situacao = 'A' or co.fechada_em > sysdate - 1)
                 order by co.aberta_em desc
                """, new { numero = i.Numero });
            if (com == default) return Results.NotFound(new { erro = $"Nenhuma comanda {i.Numero} pendente de saída." });
            if (com.Situacao == "A")
            {
                // sem consumo: encerra (cancela) e libera. Condição no próprio update evita corrida com um lançamento simultâneo.
                using (var tx = c.BeginTransaction())
                {
                    var encerrou = await c.ExecuteAsync("""
                        update comanda set situacao = 'C', fechada_em = sysdate, fechada_por = :usr, valor_total = 0,
                               motivo_cancelamento = 'Saída sem consumo', saida_em = sysdate
                         where id = :id and situacao = 'A'
                           and not exists (select 1 from comanda_item i where i.comanda_id = comanda.id and i.situacao = 'A')
                           and not exists (select 1 from comanda_pagamento pg where pg.comanda_id = comanda.id)
                        """, new { id = com.Id, usr = user.Id() }, tx);
                    if (encerrou == 1)
                    {
                        await c.ExecuteAsync("""
                            insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                            values (:usr, 'COMANDA', :id, 'CANCELAMENTO', :det)
                            """, new { usr = user.Id(), id = ((long)com.Id).ToString(),
                                      det = """{"motivo":"Saída sem consumo","origem":"CHECKOUT"}""" }, tx);
                        tx.Commit();
                        return Results.Ok(new { numero = i.Numero, cliente = com.Nome, situacao = "C", total = 0m, semConsumo = true });
                    }
                }
                var total = await c.ExecuteScalarAsync<decimal>(
                    "select nvl(sum(quantidade * preco_unitario), 0) from comanda_item where comanda_id = :id and situacao = 'A'", new { id = com.Id });
                return Results.UnprocessableEntity(new { erro = total > 0
                    ? $"Comanda {i.Numero} ({com.Nome}) ainda está ABERTA com {total:C}. Encaminhar ao caixa."
                    : $"Comanda {i.Numero} ({com.Nome}) tem pagamento prévio. Encaminhar ao caixa." });
            }
            await c.ExecuteAsync("update comanda set saida_em = sysdate where id = :id", new { id = com.Id });
            return Results.Ok(new { numero = i.Numero, cliente = com.Nome, situacao = com.Situacao, total = com.Total });
        });

        // Quem está no local agora (check-in sem check-out)
        g.MapGet("/checkin/presentes", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync("""
                select entrada_tipo "entrada", count(*) "quantidade"
                  from comanda where saida_em is null and (situacao = 'A' or fechada_em > trunc(sysdate))
                   and aberta_em > trunc(sysdate)
                 group by entrada_tipo
                """);
        });
    }

    private static async Task<Taxa> ProdutoAsync(IDbConnection c, long id, IDbTransaction tx) =>
        await c.QuerySingleAsync<Taxa>("select id produto_id, nome, preco_venda from produto where id = :id", new { id }, tx);

    private static async Task<(long? Id, string? Erro)> ObterOuCriarClienteAsync(IDbConnection c, NovoCliente n, IDbTransaction tx)
    {
        var cpf = Documento.SoDigitos(n.Cpf);
        var cel = Documento.SoDigitos(n.Celular);
        if (string.IsNullOrWhiteSpace(n.Nome)) return (null, "Informe o nome do cliente.");
        if (cpf is not null && !Documento.CpfValido(cpf)) return (null, "CPF inválido.");
        if (cel is not null && cel.Length is < 10 or > 13) return (null, "Celular deve ter DDD + número.");
        if (cpf is null && cel is null) return (null, "Para cadastrar o cliente informe CPF ou celular.");

        if (cpf is not null)
        {
            var id = await c.ExecuteScalarAsync<long?>("select id from cliente where cpf = :cpf", new { cpf }, tx);
            if (id is not null) return (id, null);
        }
        if (cel is not null)
        {
            var ids = (await c.QueryAsync<long>("select id from cliente where celular = :cel and ativo = 'S'", new { cel }, tx)).ToList();
            if (ids.Count == 1) return (ids[0], null);
        }
        var novo = await c.ExecuteScalarAsync<long>("select sq_cliente.nextval from dual", transaction: tx);
        await c.ExecuteAsync("""
            insert into cliente (id, cpf, nome, celular, email, sexo, categoria_id, aceita_comunicacao, consentimento_em, origem)
            values (:novo, :cpf, :nome, :cel, :email, :sexo, (select id from cliente_categoria where nome = 'Normal'),
                    :aceita, case when :aceita = 'S' then sysdate end, 'CHECKIN')
            """, new { novo, cpf, nome = n.Nome.Trim(), cel, email = string.IsNullOrWhiteSpace(n.Email) ? null : n.Email.Trim().ToLowerInvariant(),
                      sexo = n.Sexo is "M" or "F" or "O" ? n.Sexo : null, aceita = n.AceitaComunicacao ? "S" : "N" }, tx);
        return (novo, null);
    }

    private static async Task<IResult> SalvarEventoAsync(Db db, long? id, EventoInput i, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.Nome)) return Erro("Informe o nome do evento.");
        if (i.CouvertProdutoId is not null && (i.CouvertInicio is null || i.CouvertFim is null || i.CouvertFim <= i.CouvertInicio))
            return Erro("Couvert precisa de horário de início e fim.");
        using var c = await db.OpenAsync(ct);
        var p = new
        {
            id, data = i.Data.Date, nome = i.Nome.Trim(), terra = i.ProdutoTerraId, rio = i.ProdutoRioId, couvert = i.CouvertProdutoId,
            ini = i.CouvertInicio, fim = i.CouvertFim, ativo = i.Ativo ? "S" : "N"
        };
        try
        {
            if (id is null)
                await c.ExecuteAsync("""
                    insert into evento (data, nome, produto_terra_id, produto_rio_id, couvert_produto_id, couvert_inicio, couvert_fim, ativo)
                    values (:data, :nome, :terra, :rio, :couvert, :ini, :fim, :ativo)
                    """, p);
            else
                await c.ExecuteAsync("""
                    update evento set data = :data, nome = :nome, produto_terra_id = :terra, produto_rio_id = :rio,
                           couvert_produto_id = :couvert, couvert_inicio = :ini, couvert_fim = :fim, ativo = :ativo
                     where id = :id
                    """, p);
        }
        catch (OracleException e) when (e.Number == 1)
        {
            return Results.Conflict(new { erro = "Já existe evento nesta data." });
        }
        return Results.Ok();
    }

    private static IResult Erro(string msg) => Results.UnprocessableEntity(new { erro = msg });
}
