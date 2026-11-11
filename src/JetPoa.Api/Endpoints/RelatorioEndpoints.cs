using Dapper;
using JetPoa.Api.Data;

namespace JetPoa.Api.Endpoints;

/// <summary>Relatórios gerenciais por período (comandas fechadas entre :de e :ate, inclusive).</summary>
public static class RelatorioEndpoints
{
    public sealed class Resumo
    {
        public int Comandas { get; set; }
        public int ConsumoInterno { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Descontos { get; set; }
        public decimal TaxaServico { get; set; }
        public decimal Faturamento { get; set; }
        public decimal TicketMedio { get; set; }
        public decimal Custo { get; set; }
        public int ItensCancelados { get; set; }
        public decimal ValorCancelado { get; set; }
        public int NfceAutorizadas { get; set; }
        public decimal ValorNfce { get; set; }
    }

    public sealed class Linha
    {
        public string Chave { get; set; } = "";
        public string? Grupo { get; set; }
        public int Qtd { get; set; }
        public decimal Quantidade { get; set; }
        public decimal Valor { get; set; }
        public decimal? Custo { get; set; }
    }

    public static void MapRelatorios(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/relatorios/vendas", async (Db db, DateTime? de, DateTime? ate, CancellationToken ct) =>
        {
            var ini = (de ?? DateTime.Today).Date;
            var fim = (ate ?? ini).Date.AddDays(1);
            if (fim <= ini) return Results.UnprocessableEntity(new { erro = "Período inválido." });
            if ((fim - ini).TotalDays > 400) return Results.UnprocessableEntity(new { erro = "Período máximo de 400 dias." });
            var p = new { ini, fim };

            using var c = await db.OpenAsync(ct);
            const string F = "co.situacao = 'F' and co.fechada_em >= :ini and co.fechada_em < :fim";
            // valor líquido de cada item = quantidade x preço - desconto rateado
            const string Liq = "(i.quantidade * i.preco_unitario - i.valor_desconto)";

            // Oracle 11g: agregados num bloco e subconsultas em outro (ORA-00937 se misturados)
            var resumo = await c.QuerySingleAsync<Resumo>($"""
                select a.*, k.custo, x.itens_cancelados, x.valor_cancelado, n.nfce_autorizadas, n.valor_nfce
                  from (select count(*) comandas,
                               nvl(sum(case when co.consumo_interno = 'S' then 1 else 0 end), 0) consumo_interno,
                               nvl(sum(co.subtotal), 0) subtotal, nvl(sum(co.valor_desconto), 0) descontos,
                               nvl(sum(co.valor_taxa_servico), 0) taxa_servico, nvl(sum(co.valor_total), 0) faturamento,
                               case when count(*) > 0 then round(sum(co.valor_total) / count(*), 2) else 0 end ticket_medio
                          from comanda co where {F}) a,
                       (select nvl(sum(i.quantidade * nvl(p.preco_custo, 0)), 0) custo
                          from comanda_item i join produto p on p.id = i.produto_id join comanda co on co.id = i.comanda_id
                         where i.situacao = 'A' and {F}) k,
                       (select count(*) itens_cancelados, nvl(sum(i.quantidade * i.preco_unitario), 0) valor_cancelado
                          from comanda_item i where i.situacao = 'C' and i.cancelado_em >= :ini and i.cancelado_em < :fim) x,
                       (select count(*) nfce_autorizadas, nvl(sum(valor_total), 0) valor_nfce
                          from nfce where situacao = 'AUTORIZADA' and ambiente = '1' and emitida_em >= :ini and emitida_em < :fim) n
                """, p);

            async Task<IEnumerable<Linha>> Q(string sql) => await c.QueryAsync<Linha>(sql, p);

            var porDia = await Q($"""
                select to_char(trunc(co.fechada_em), 'yyyy-mm-dd') chave, count(*) qtd, sum(co.valor_total) valor
                  from comanda co where {F} group by trunc(co.fechada_em) order by 1
                """);
            var porHora = await Q($"""
                select to_char(co.fechada_em, 'HH24') || 'h' chave, count(*) qtd, sum(co.valor_total) valor
                  from comanda co where {F} group by to_char(co.fechada_em, 'HH24') order by 1
                """);
            var porForma = await Q($"""
                select f.nome chave, count(*) qtd, sum(pg.valor) valor
                  from comanda_pagamento pg join forma_pagamento f on f.id = pg.forma_pagamento_id join comanda co on co.id = pg.comanda_id
                 where {F} group by f.nome order by 3 desc
                """);
            var porEntrada = await Q($"""
                select case co.entrada_tipo when 'R' then 'Rio' else 'Terra' end chave, count(*) qtd, sum(co.valor_total) valor
                  from comanda co where {F} group by co.entrada_tipo order by 3 desc
                """);
            var porCategoria = await Q($"""
                select cat.nome chave, count(*) qtd, sum(i.quantidade) quantidade, sum({Liq}) valor,
                       sum(i.quantidade * nvl(p.preco_custo, 0)) custo
                  from comanda_item i join comanda co on co.id = i.comanda_id join produto p on p.id = i.produto_id
                  join categoria cat on cat.id = p.categoria_id
                 where i.situacao = 'A' and {F} group by cat.nome order by 4 desc
                """);
            var porProduto = await Q($"""
                select * from (
                  select p.nome chave, cat.nome grupo, count(*) qtd, sum(i.quantidade) quantidade, sum({Liq}) valor,
                         sum(i.quantidade * nvl(p.preco_custo, 0)) custo
                    from comanda_item i join comanda co on co.id = i.comanda_id join produto p on p.id = i.produto_id
                    join categoria cat on cat.id = p.categoria_id
                   where i.situacao = 'A' and {F} group by p.nome, cat.nome order by 5 desc
                ) where rownum <= 100
                """);
            var porGarcom = await Q($"""
                select nvl(g.nome, 'Caixa / sem garçom') chave, count(distinct co.id) qtd, sum(i.quantidade) quantidade, sum({Liq}) valor
                  from comanda_item i join comanda co on co.id = i.comanda_id left join garcom g on g.id = i.garcom_id
                 where i.situacao = 'A' and {F} group by nvl(g.nome, 'Caixa / sem garçom') order by 4 desc
                """);
            var descontosPorMotivo = await Q($"""
                select nvl(m.nome, case d.origem when 'CATEGORIA' then 'Automático (categoria do cliente)'
                                                 when 'INTERNO' then 'Consumo interno' when 'ISENCAO' then 'Isenção de taxa' else d.origem end) chave,
                       d.origem grupo, count(*) qtd, sum(d.valor_calculado) valor
                  from comanda_desconto d join comanda co on co.id = d.comanda_id left join motivo_desconto m on m.id = d.motivo_id
                 where {F} group by nvl(m.nome, case d.origem when 'CATEGORIA' then 'Automático (categoria do cliente)'
                                                 when 'INTERNO' then 'Consumo interno' when 'ISENCAO' then 'Isenção de taxa' else d.origem end), d.origem
                 order by 4 desc
                """);
            var descontosPorOperador = await Q($"""
                select u.nome chave, a.nome grupo, count(*) qtd, sum(co.valor_desconto) valor
                  from comanda co join usuario u on u.id = co.fechada_por left join usuario a on a.id = co.desconto_autorizado_por
                 where {F} and co.valor_desconto > 0 group by u.nome, a.nome order by 4 desc
                """);

            return Results.Ok(new { de = ini, ate = fim.AddDays(-1), resumo, porDia, porHora, porForma, porEntrada, porCategoria,
                                    porProduto, porGarcom, descontosPorMotivo, descontosPorOperador });
        }).WithTags("Relatórios");
    }
}
