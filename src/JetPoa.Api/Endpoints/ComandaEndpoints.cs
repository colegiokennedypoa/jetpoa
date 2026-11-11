using System.Data;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Impressao;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Endpoints;

public static class ComandaEndpoints
{
    public sealed class ComandaResumo
    {
        public long Id { get; set; }
        public int Numero { get; set; }
        public long? ClienteId { get; set; }
        public string? Identificacao { get; set; }
        public string? ClienteCategoria { get; set; }
        public string EntradaTipo { get; set; } = "T";
        public long? AreaId { get; set; }
        public string? Area { get; set; }
        public string Situacao { get; set; } = "A";
        public string? Observacao { get; set; }
        public DateTime AbertaEm { get; set; }
        public decimal Total { get; set; }
        public int QtdItens { get; set; }
        public int EnviosComErro { get; set; }
        public string? ClienteCpf { get; set; }
        public decimal? ValorTotal { get; set; }
        public DateTime? FechadaEm { get; set; }
    }

    public sealed class ComandaItem
    {
        public long Id { get; set; }
        public long ProdutoId { get; set; }
        public string Produto { get; set; } = "";
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Total { get; set; }
        public string? Observacao { get; set; }
        public string? Garcom { get; set; }
        public string Situacao { get; set; } = "A";
        public string? MotivoCancelamento { get; set; }
        public DateTime LancadoEm { get; set; }
        public string Origem { get; set; } = "CAIXA";
        public string Setor { get; set; } = "";
        /// <summary>null = setor não imprime; P/I/E = situação do envio para produção.</summary>
        public string? Impressao { get; set; }
        public string? ErroImpressao { get; set; }
        public string? TaxaTipo { get; set; }
        public decimal ValorDesconto { get; set; }
    }

    public sealed record AbrirComandaInput(int Numero, long? ClienteId, string? Identificacao, string? EntradaTipo, long? AreaId, string? Observacao);
    public sealed record AlterarComandaInput(long? ClienteId, string? Identificacao, string? EntradaTipo, long? AreaId, string? Observacao);
    public sealed record LancarItem(long ProdutoId, decimal Quantidade, string? Observacao);
    public sealed record LancarInput(long? GarcomId, string? Origem, List<LancarItem> Itens);
    public sealed record CancelarItemInput(string Motivo);

    private sealed class ProdutoLanc
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public decimal PrecoVenda { get; set; }
        public string Ativo { get; set; } = "S";
        public string Vendivel { get; set; } = "S";
        public long SetorProducaoId { get; set; }
        public string Imprime { get; set; } = "N";
    }

    private sealed class ItemCancelar
    {
        public string Situacao { get; set; } = "";
        public long? EnvioId { get; set; }
        public long SetorId { get; set; }
        public string Imprime { get; set; } = "N";
        public string ComandaSituacao { get; set; } = "";
    }

    private const string SelectResumo = """
        select co.id, co.numero, co.cliente_id, nvl(cl.nome, co.identificacao) identificacao,
               cc.nome cliente_categoria, co.entrada_tipo, co.area_id, a.nome area, co.situacao,
               co.observacao, co.aberta_em,
               nvl((select sum(i.quantidade * i.preco_unitario) from comanda_item i
                     where i.comanda_id = co.id and i.situacao = 'A'), 0) total,
               (select count(*) from comanda_item i where i.comanda_id = co.id and i.situacao = 'A') qtd_itens,
               (select count(*) from envio_producao e where e.comanda_id = co.id and e.situacao = 'E') envios_com_erro,
               cl.cpf cliente_cpf, co.valor_total, co.fechada_em
          from comanda co
          left join cliente cl on cl.id = co.cliente_id
          left join cliente_categoria cc on cc.id = cl.categoria_id
          left join area a on a.id = co.area_id
        """;

    public static void MapComandas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/comandas").WithTags("Comandas");

        g.MapGet("", async (Db db, string? situacao, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<ComandaResumo>(
                SelectResumo + """
                     where co.situacao = :situacao
                       and (co.situacao = 'A' or co.fechada_em > sysdate - 1)
                     order by case when co.situacao = 'A' then co.numero end, co.fechada_em desc
                    """,
                new { situacao = string.IsNullOrWhiteSpace(situacao) ? "A" : situacao });
        });

        g.MapGet("/{id:long}", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var com = await c.QuerySingleOrDefaultAsync<ComandaResumo>(SelectResumo + " where co.id = :id", new { id });
            if (com is null) return Results.NotFound();
            var itens = await c.QueryAsync<ComandaItem>("""
                select i.id, i.produto_id, p.nome produto, i.quantidade, i.preco_unitario,
                       round(i.quantidade * i.preco_unitario, 2) total, i.observacao, g.nome garcom,
                       i.situacao, i.motivo_cancelamento, i.lancado_em, i.origem, s.codigo setor,
                       e.situacao impressao, e.ultimo_erro erro_impressao, p.taxa_tipo, i.valor_desconto
                  from comanda_item i
                  join produto p on p.id = i.produto_id
                  join setor_producao s on s.id = p.setor_producao_id
                  left join garcom g on g.id = i.garcom_id
                  left join envio_producao e on e.id = i.envio_id
                 where i.comanda_id = :id
                 order by i.id
                """, new { id });
            return Results.Ok(new { comanda = com, itens });
        });

        g.MapGet("/numero/{numero:int}", async (Db db, int numero, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var com = await c.QuerySingleOrDefaultAsync<ComandaResumo>(
                SelectResumo + " where co.numero = :numero and co.situacao = 'A'", new { numero });
            return com is null
                ? Results.NotFound(new { erro = $"Comanda {numero} não está aberta. Abra no caixa ou no check-in." })
                : Results.Ok(com);
        });

        g.MapPost("", async (Db db, AbrirComandaInput i, CancellationToken ct) =>
        {
            if (i.Numero <= 0) return Erro("Informe o número da comanda.");
            var entrada = NormalizarEntrada(i.EntradaTipo);
            if (entrada is null) return Erro("Entrada deve ser T (Terra) ou R (Rio).");
            // regra do JetPOA: comanda só para cliente previamente cadastrado
            if (i.ClienteId is null) return Erro("Selecione um cliente cadastrado (ou cadastre-o antes de abrir a comanda).");

            using var c = await db.OpenAsync(ct);
            var clienteAtivo = await c.ExecuteScalarAsync<string?>("select ativo from cliente where id = :id", new { id = i.ClienteId });
            if (clienteAtivo is null) return Erro("Cliente não encontrado.");
            if (clienteAtivo != "S") return Erro("Cliente inativo.");
            var id = await c.ExecuteScalarAsync<long>("select sq_comanda.nextval from dual");
            try
            {
                await c.ExecuteAsync("""
                    insert into comanda (id, numero, cliente_id, identificacao, entrada_tipo, area_id, observacao)
                    values (:id, :numero, :cliente_id, :identificacao, :entrada, :area_id, :observacao)
                    """, new
                {
                    id, numero = i.Numero, cliente_id = i.ClienteId, identificacao = Limpo(i.Identificacao),
                    entrada, area_id = i.AreaId, observacao = Limpo(i.Observacao)
                });
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = $"A comanda {i.Numero} já está aberta." });
            }
            catch (OracleException e) when (e.Number == 2291)
            {
                return Erro("Cliente ou área inexistente.");
            }
            return Results.Created($"/api/comandas/{id}", new { id });
        });

        g.MapPatch("/{id:long}", async (Db db, long id, AlterarComandaInput i, CancellationToken ct) =>
        {
            var entrada = i.EntradaTipo is null ? null : NormalizarEntrada(i.EntradaTipo);
            if (i.EntradaTipo is not null && entrada is null) return Erro("Entrada deve ser T (Terra) ou R (Rio).");
            using var c = await db.OpenAsync(ct);
            if (i.ClienteId is not null &&
                await c.ExecuteScalarAsync<string?>("select ativo from cliente where id = :id", new { id = i.ClienteId }) != "S")
                return Erro("Cliente não encontrado ou inativo.");
            var semCliente = await c.ExecuteScalarAsync<int>(
                "select count(*) from comanda where id = :id and cliente_id is null", new { id });
            if (semCliente > 0 && i.ClienteId is null) return Erro("Esta comanda está sem cliente: selecione um cliente cadastrado.");
            var n = await c.ExecuteAsync("""
                update comanda
                   set cliente_id = nvl(:cliente_id, cliente_id),
                       identificacao = :identificacao,
                       entrada_tipo = nvl(:entrada, entrada_tipo),
                       area_id = :area_id,
                       observacao = :observacao
                 where id = :id and situacao = 'A'
                """, new { id, cliente_id = i.ClienteId, identificacao = Limpo(i.Identificacao), entrada, area_id = i.AreaId, observacao = Limpo(i.Observacao) });
            return n == 0 ? Results.NotFound(new { erro = "Comanda não encontrada ou não está aberta." }) : Results.NoContent();
        });

        // Lança itens e gera um envio de produção por setor que imprime
        g.MapPost("/{id:long}/itens", async (Db db, ImpressaoProducao impressao, long id, LancarInput input, CancellationToken ct) =>
        {
            var itens = input.Itens?.Where(x => x.Quantidade > 0).ToList() ?? [];
            if (itens.Count == 0) return Erro("Nenhum item para lançar.");
            if (itens.Any(x => x.Quantidade > 999)) return Erro("Quantidade inválida.");
            var origem = string.Equals(input.Origem, "GARCOM", StringComparison.OrdinalIgnoreCase) ? "GARCOM" : "CAIXA";

            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();

            var situacao = await c.ExecuteScalarAsync<string?>(
                "select situacao from comanda where id = :id for update", new { id }, tx);
            if (situacao is null) return Results.NotFound(new { erro = "Comanda não encontrada." });
            if (situacao != "A") return Erro("Comanda não está aberta.");

            var ids = itens.Select(x => x.ProdutoId).Distinct().ToList();
            var produtos = (await c.QueryAsync<ProdutoLanc>("""
                select p.id, p.nome, p.preco_venda, p.ativo, p.vendivel, p.setor_producao_id, s.imprime
                  from produto p join setor_producao s on s.id = p.setor_producao_id
                 where p.id in :ids
                """, new { ids }, tx)).ToDictionary(p => p.Id);

            var invalido = ids.FirstOrDefault(pid => !produtos.TryGetValue(pid, out var p) || p.Ativo != "S" || p.Vendivel != "S");
            if (invalido != 0) return Erro($"Produto {invalido} inexistente, inativo ou não vendável.");

            // limite da comanda: categoria do cliente ou parâmetro geral
            var limite = await c.ExecuteScalarAsync<decimal?>("""
                select nvl(cc.limite_comanda, to_number((select valor from parametro where chave = 'LIMITE_COMANDA')))
                  from comanda co
                  left join cliente cl on cl.id = co.cliente_id
                  left join cliente_categoria cc on cc.id = cl.categoria_id
                 where co.id = :id
                """, new { id }, tx);
            if (limite is > 0)
            {
                var atual = await c.ExecuteScalarAsync<decimal>(
                    "select nvl(sum(quantidade * preco_unitario), 0) from comanda_item where comanda_id = :id and situacao = 'A'",
                    new { id }, tx);
                var novo = itens.Sum(x => x.Quantidade * produtos[x.ProdutoId].PrecoVenda);
                if (atual + novo > limite)
                    return Erro($"Comanda atingiria {atual + novo:C} e o limite é {limite:C}. Dirija-se ao caixa.");
            }

            // um envio por setor que imprime
            var envioPorSetor = new Dictionary<long, long>();
            foreach (var setor in itens.Select(x => produtos[x.ProdutoId]).Where(p => p.Imprime == "S")
                                       .Select(p => p.SetorProducaoId).Distinct())
            {
                var envioId = await c.ExecuteScalarAsync<long>("select sq_envio_producao.nextval from dual", transaction: tx);
                await c.ExecuteAsync(
                    "insert into envio_producao (id, comanda_id, setor_producao_id, tipo) values (:envioId, :id, :setor, 'P')",
                    new { envioId, id, setor }, tx);
                envioPorSetor[setor] = envioId;
            }

            foreach (var x in itens)
            {
                var p = produtos[x.ProdutoId];
                await c.ExecuteAsync("""
                    insert into comanda_item (comanda_id, produto_id, quantidade, preco_unitario, observacao, garcom_id, envio_id, origem)
                    values (:id, :produto_id, :quantidade, :preco, :observacao, :garcom_id, :envio_id, :origem)
                    """, new
                {
                    id, produto_id = p.Id, quantidade = x.Quantidade, preco = p.PrecoVenda,
                    observacao = Limpo(x.Observacao), garcom_id = input.GarcomId,
                    envio_id = envioPorSetor.TryGetValue(p.SetorProducaoId, out var e) ? e : (long?)null,
                    origem
                }, tx);
            }

            tx.Commit();
            if (envioPorSetor.Count > 0) impressao.Notificar();
            return Results.Ok(new { lancados = itens.Count, envios = envioPorSetor.Values });
        });

        // Cancela um item; se já tinha ido para produção, imprime aviso de cancelamento no setor
        g.MapPost("/{id:long}/itens/{itemId:long}/cancelar", async (Db db, ImpressaoProducao impressao, long id, long itemId, CancelarItemInput input, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Motivo) || input.Motivo.Trim().Length < 3)
                return Erro("Informe o motivo do cancelamento.");

            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            var item = await c.QuerySingleOrDefaultAsync<ItemCancelar>("""
                select i.situacao, i.envio_id, p.setor_producao_id setor_id, s.imprime, co.situacao comanda_situacao
                  from comanda_item i
                  join comanda co on co.id = i.comanda_id
                  join produto p on p.id = i.produto_id
                  join setor_producao s on s.id = p.setor_producao_id
                 where i.id = :itemId and i.comanda_id = :id
                   for update of i.situacao
                """, new { id, itemId }, tx);
            if (item is null) return Results.NotFound(new { erro = "Item não encontrado." });
            if (item.ComandaSituacao != "A") return Erro("Comanda não está aberta.");
            if (item.Situacao != "A") return Erro("Item já cancelado.");

            long? envioCanc = null;
            if (item.EnvioId is not null && item.Imprime == "S")
            {
                envioCanc = await c.ExecuteScalarAsync<long>("select sq_envio_producao.nextval from dual", transaction: tx);
                await c.ExecuteAsync(
                    "insert into envio_producao (id, comanda_id, setor_producao_id, tipo) values (:envioCanc, :id, :setor, 'C')",
                    new { envioCanc, id, setor = item.SetorId }, tx);
            }

            await c.ExecuteAsync("""
                update comanda_item
                   set situacao = 'C', cancelado_em = sysdate, motivo_cancelamento = :motivo, envio_cancelamento_id = :envioCanc
                 where id = :itemId
                """, new { itemId, motivo = input.Motivo.Trim(), envioCanc }, tx);

            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (null, 'COMANDA_ITEM', :itemId, 'CANCELAMENTO', :detalhe)
                """, new { itemId = itemId.ToString(), detalhe = $"{{\"comanda\":{id},\"motivo\":\"{input.Motivo.Trim().Replace("\"", "'")}\"}}" }, tx);

            tx.Commit();
            if (envioCanc is not null) impressao.Notificar();
            return Results.NoContent();
        });
    }

    private static string? NormalizarEntrada(string? e) => (e ?? "T").Trim().ToUpperInvariant() switch
    {
        "T" or "TERRA" => "T",
        "R" or "RIO" or "A" or "AGUA" or "ÁGUA" => "R",
        _ => null
    };

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static IResult Erro(string msg) => Results.UnprocessableEntity(new { erro = msg });
}
