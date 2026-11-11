using System.Data;
using Dapper;

namespace JetPoa.Api.Domain;

/// <summary>Movimentação de estoque. Só produtos com controla_estoque = 'S' têm saldo.</summary>
public static class Estoque
{
    private sealed class Baixa
    {
        public long ProdutoId { get; set; }
        public decimal Quantidade { get; set; }
    }

    /// <summary>Baixa a venda de uma comanda fechada. Kit baixa os componentes; produto simples baixa a si mesmo.</summary>
    public static async Task BaixarVendaAsync(IDbConnection c, long comandaId, long? usuarioId, IDbTransaction tx)
    {
        var baixas = (await c.QueryAsync<Baixa>("""
            select produto_id, sum(quantidade) quantidade from (
                -- produtos simples
                select i.produto_id, i.quantidade
                  from comanda_item i join produto p on p.id = i.produto_id
                 where i.comanda_id = :comandaId and i.situacao = 'A' and p.tipo = 'P' and p.controla_estoque = 'S'
                union all
                -- componentes dos kits/combos
                select pc.componente_id, i.quantidade * pc.quantidade
                  from comanda_item i
                  join produto p on p.id = i.produto_id
                  join produto_componente pc on pc.produto_id = p.id
                  join produto comp on comp.id = pc.componente_id
                 where i.comanda_id = :comandaId and i.situacao = 'A' and p.tipo = 'K' and comp.controla_estoque = 'S'
            ) group by produto_id
            """, new { comandaId }, tx)).ToList();

        foreach (var b in baixas)
            await MovimentarAsync(c, b.ProdutoId, "V", -b.Quantidade, null, comandaId, null, usuarioId, tx);
    }

    /// <summary>Lança um movimento (quantidade com sinal) e atualiza o saldo do produto. Retorna o novo saldo.</summary>
    public static async Task<decimal> MovimentarAsync(IDbConnection c, long produtoId, string tipo, decimal quantidade,
        decimal? custoUnitario, long? comandaId, string? observacao, long? usuarioId, IDbTransaction tx)
    {
        var saldo = await c.ExecuteScalarAsync<decimal>(
            "select estoque_atual from produto where id = :produtoId for update", new { produtoId }, tx) + quantidade;
        await c.ExecuteAsync("""
            update produto set estoque_atual = :saldo,
                   preco_custo = case when :tipo = 'E' and :custo is not null then :custo else preco_custo end,
                   alterado_em = sysdate
             where id = :produtoId
            """, new { produtoId, saldo, tipo, custo = custoUnitario }, tx);
        await c.ExecuteAsync("""
            insert into movimento_estoque (produto_id, tipo, quantidade, saldo_apos, custo_unitario, comanda_id, observacao, usuario_id)
            values (:produtoId, :tipo, :quantidade, :saldo, :custo, :comandaId, :obs, :usuarioId)
            """, new { produtoId, tipo, quantidade, saldo, custo = custoUnitario, comandaId, obs = observacao, usuarioId }, tx);
        return saldo;
    }
}
