using System.Security.Claims;
using JetPoa.Api.Auth;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;

namespace JetPoa.Api.Endpoints;

public static class EstoqueEndpoints
{
    public sealed class ItemEstoque
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string Unidade { get; set; } = "UN";
        public decimal EstoqueAtual { get; set; }
        public decimal? EstoqueMinimo { get; set; }
        public decimal? PrecoCusto { get; set; }
        public decimal PrecoVenda { get; set; }
        public decimal? Comprar { get; set; }
        public decimal VendidoHoje { get; set; }
        public DateTime? UltimoMovimento { get; set; }
    }

    public sealed class Movimento
    {
        public long Id { get; set; }
        public DateTime CriadoEm { get; set; }
        public string Produto { get; set; } = "";
        public string Tipo { get; set; } = "";
        public decimal Quantidade { get; set; }
        public decimal SaldoApos { get; set; }
        public decimal? CustoUnitario { get; set; }
        public int? ComandaNumero { get; set; }
        public string? Observacao { get; set; }
        public string? Usuario { get; set; }
    }

    public sealed record MovimentoInput(long ProdutoId, string Tipo, decimal Quantidade, decimal? CustoUnitario, string? Observacao);
    public sealed record ContagemItem(long ProdutoId, decimal Contado);
    public sealed record InventarioInput(List<ContagemItem> Itens, string? Observacao);

    public static void MapEstoque(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque").WithTags("Estoque").RequireAuthorization(Permissoes.EstoqueVer);
        var gMov = g.MapGroup("").RequireAuthorization(Permissoes.EstoqueMovimentar);
        var gAjuste = g.MapGroup("").RequireAuthorization(Permissoes.EstoqueAjustar);

        g.MapGet("", async (Db db, bool? abaixoMinimo, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<ItemEstoque>("""
                select p.id, p.nome, c.nome categoria, p.unidade, p.estoque_atual, p.estoque_minimo, p.preco_custo, p.preco_venda,
                       case when p.estoque_minimo > p.estoque_atual then p.estoque_minimo - p.estoque_atual end comprar,
                       nvl((select -sum(m.quantidade) from movimento_estoque m
                             where m.produto_id = p.id and m.tipo = 'V' and m.criado_em >= trunc(sysdate)), 0) vendido_hoje,
                       (select max(m.criado_em) from movimento_estoque m where m.produto_id = p.id) ultimo_movimento
                  from produto p join categoria c on c.id = p.categoria_id
                 where p.ativo = 'S' and p.controla_estoque = 'S'
                   and (:abaixo = 0 or p.estoque_minimo > p.estoque_atual)
                 order by c.ordem, p.nome
                """, new { abaixo = abaixoMinimo == true ? 1 : 0 });
        });

        g.MapGet("/movimentos", async (Db db, long? produtoId, int? dias, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Movimento>("""
                select * from (
                  select m.id, m.criado_em, p.nome produto, m.tipo, m.quantidade, m.saldo_apos, m.custo_unitario,
                         co.numero comanda_numero, m.observacao, u.nome usuario
                    from movimento_estoque m
                    join produto p on p.id = m.produto_id
                    left join comanda co on co.id = m.comanda_id
                    left join usuario u on u.id = m.usuario_id
                   where (:produtoId is null or m.produto_id = :produtoId)
                     and m.criado_em > sysdate - :dias
                   order by m.id desc
                ) where rownum <= 500
                """, new { produtoId, dias = Math.Clamp(dias ?? 7, 1, 365) });
        });

        // Entrada de compra (E), perda/quebra (P) ou ajuste manual (A)
        gMov.MapPost("/movimento", async (ClaimsPrincipal user, Db db, MovimentoInput i, CancellationToken ct) =>
        {
            var tipo = (i.Tipo ?? "").ToUpperInvariant();
            if (tipo is not ("E" or "P" or "A")) return Erro("Tipo deve ser E (entrada), P (perda) ou A (ajuste).");
            if (i.Quantidade == 0) return Erro("Quantidade não pode ser zero.");
            if (tipo is "E" or "P" && i.Quantidade < 0) return Erro("Informe a quantidade positiva.");
            if (tipo is "P" or "A" && string.IsNullOrWhiteSpace(i.Observacao)) return Erro("Informe o motivo.");
            if (i.CustoUnitario is < 0) return Erro("Custo inválido.");

            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            if (tipo == "A" && !user.Pode(Permissoes.EstoqueAjustar)) return Results.Forbid();
            var controla = await c.ExecuteScalarAsync<string?>("select controla_estoque from produto where id = :id", new { id = i.ProdutoId }, tx);
            if (controla != "S") return Erro("Produto não controla estoque (kits baixam os componentes).");

            var qtd = tipo == "P" ? -i.Quantidade : i.Quantidade;
            var saldo = await Estoque.MovimentarAsync(c, i.ProdutoId, tipo, qtd, tipo == "E" ? i.CustoUnitario : null, null,
                string.IsNullOrWhiteSpace(i.Observacao) ? null : i.Observacao.Trim(), user.Id(), tx);
            tx.Commit();
            return Results.Ok(new { saldo });
        });

        // Inventário: informa o contado; o sistema lança a diferença (tipo I)
        gAjuste.MapPost("/inventario", async (ClaimsPrincipal user, Db db, InventarioInput i, CancellationToken ct) =>
        {
            var itens = i.Itens?.Where(x => x.Contado >= 0).ToList() ?? [];
            if (itens.Count == 0) return Erro("Nenhuma contagem informada.");
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();

            var ajustes = new List<object>();
            foreach (var x in itens)
            {
                var atual = await c.ExecuteScalarAsync<decimal?>(
                    "select estoque_atual from produto where id = :id and controla_estoque = 'S'", new { id = x.ProdutoId }, tx);
                if (atual is null) continue;
                var dif = x.Contado - atual.Value;
                if (dif == 0) continue;
                await Estoque.MovimentarAsync(c, x.ProdutoId, "I", dif, null, null,
                    $"Inventário: sistema {atual.Value:0.###}, contado {x.Contado:0.###}" + (string.IsNullOrWhiteSpace(i.Observacao) ? "" : " · " + i.Observacao.Trim()),
                    user.Id(), tx);
                ajustes.Add(new { x.ProdutoId, sistema = atual.Value, contado = x.Contado, diferenca = dif });
            }
            tx.Commit();
            return Results.Ok(new { ajustados = ajustes.Count, ajustes });
        });
    }

    private static IResult Erro(string msg) => Results.UnprocessableEntity(new { erro = msg });
}
