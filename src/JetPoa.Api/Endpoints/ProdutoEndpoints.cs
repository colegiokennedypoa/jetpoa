using System.Security.Claims;
using JetPoa.Api.Auth;
using System.Text.Json;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Fiscal;

namespace JetPoa.Api.Endpoints;

/// <summary>Cadastro/edição de produto (preço, setor de produção, estoque mínimo e dados fiscais).</summary>
public static class ProdutoEndpoints
{
    public sealed class Produto
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string? Descricao { get; set; }
        public long CategoriaId { get; set; }
        public string Tipo { get; set; } = "P";
        public long SetorProducaoId { get; set; }
        public string Unidade { get; set; } = "UN";
        public decimal PrecoVenda { get; set; }
        public decimal? PrecoCusto { get; set; }
        public string ControlaEstoque { get; set; } = "S";
        public decimal EstoqueAtual { get; set; }
        public decimal? EstoqueMinimo { get; set; }
        public string Vendivel { get; set; } = "S";
        public string Ativo { get; set; } = "S";
        public int Ordem { get; set; }
        public string? Ncm { get; set; }
        public string? Cest { get; set; }
        public string? Cfop { get; set; }
        public string? Csosn { get; set; }
        public string? CstPisCofins { get; set; }
        public string? Gtin { get; set; }
        public string? TaxaTipo { get; set; }
        public long? LegadoCdProduto { get; set; }
    }

    public sealed record ProdutoInput(
        string Nome, string? Descricao, long CategoriaId, string Tipo, long SetorProducaoId, string? Unidade,
        decimal PrecoVenda, decimal? PrecoCusto, bool ControlaEstoque, decimal? EstoqueMinimo, bool Vendivel, bool Ativo, int? Ordem,
        string? Ncm, string? Cest, string? Cfop, string? Csosn, string? CstPisCofins, string? Gtin);

    private const string Select = """
        select id, nome, descricao, categoria_id, tipo, setor_producao_id, unidade, preco_venda, preco_custo, controla_estoque,
               estoque_atual, estoque_minimo, vendivel, ativo, ordem, ncm, cest, cfop, csosn, cst_pis_cofins, gtin, taxa_tipo, legado_cd_produto
          from produto
        """;

    public static void MapProdutos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/produtos").WithTags("Catálogo").RequireAuthorization(Permissoes.ProdutosVer);
        var gEditar = g.MapGroup("").RequireAuthorization(Permissoes.ProdutosEditar);

        g.MapGet("/{id:long}", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var p = await c.QuerySingleOrDefaultAsync<Produto>(Select + " where id = :id", new { id });
            return p is null ? Results.NotFound() : Results.Ok(p);
        });

        gEditar.MapPost("", async (ClaimsPrincipal user, Db db, ProdutoInput i, CancellationToken ct) => await SalvarAsync(db, null, i, user.Id(), ct));
        gEditar.MapPut("/{id:long}", async (ClaimsPrincipal user, Db db, long id, ProdutoInput i, CancellationToken ct) => await SalvarAsync(db, id, i, user.Id(), ct));
    }

    private static async Task<IResult> SalvarAsync(Db db, long? id, ProdutoInput i, long usuarioId, CancellationToken ct)
    {
        string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(char.IsLetterOrDigit).ToArray());
        var ncm = Limpo(i.Ncm); var cest = Limpo(i.Cest); var cfop = Limpo(i.Cfop); var csosn = Limpo(i.Csosn);
        var cst = Limpo(i.CstPisCofins); var gtin = Limpo(i.Gtin);

        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(i.Nome)) erros.Add("Informe o nome.");
        if (i.PrecoVenda < 0) erros.Add("Preço não pode ser negativo (desconto é feito no fechamento).");
        if (i.Tipo is not ("P" or "K" or "S")) erros.Add("Tipo deve ser P, K ou S.");
        if (ncm is not null && (ncm.Length != 8 || !ncm.All(char.IsDigit))) erros.Add("NCM deve ter 8 dígitos.");
        if (cfop is not null && (cfop.Length != 4 || !cfop.All(char.IsDigit))) erros.Add("CFOP deve ter 4 dígitos.");
        if (cest is not null && cest.Length != 7) erros.Add("CEST deve ter 7 dígitos.");
        if (gtin is not null && gtin.Length is not (8 or 12 or 13 or 14)) erros.Add("GTIN/EAN deve ter 8, 12, 13 ou 14 dígitos.");
        if (erros.Count > 0) return Results.UnprocessableEntity(new { erro = string.Join(" ", erros) });

        // aviso fiscal (não bloqueia salvar; bloqueia só a emissão da NFC-e)
        var avisos = i.Tipo == "S" ? new List<string>() : NfceXml.Validar([(i.Nome, ncm, cfop, csosn, cst)]);

        using var c = await db.OpenAsync(ct);
        using var tx = c.BeginTransaction();
        var antes = id is null ? null : await c.QuerySingleOrDefaultAsync<Produto>(Select + " where id = :id", new { id }, tx);
        if (id is not null && antes is null) return Results.NotFound();

        var p = new
        {
            id, nome = i.Nome.Trim().ToUpperInvariant(), descricao = string.IsNullOrWhiteSpace(i.Descricao) ? null : i.Descricao.Trim(),
            categoria = i.CategoriaId, tipo = i.Tipo, setor = i.SetorProducaoId, unidade = string.IsNullOrWhiteSpace(i.Unidade) ? "UN" : i.Unidade.Trim().ToUpperInvariant(),
            preco = i.PrecoVenda, custo = i.PrecoCusto, controla = i.Tipo == "P" && i.ControlaEstoque ? "S" : "N", minimo = i.EstoqueMinimo,
            vendivel = i.Vendivel ? "S" : "N", ativo = i.Ativo ? "S" : "N", ordem = i.Ordem ?? 0,
            ncm, cest, cfop, csosn, cst, gtin, usr = usuarioId
        };
        long novoId;
        if (id is null)
        {
            novoId = await c.ExecuteScalarAsync<long>("select sq_produto.nextval from dual", transaction: tx);
            await c.ExecuteAsync("""
                insert into produto (id, nome, descricao, categoria_id, tipo, setor_producao_id, unidade, preco_venda, preco_custo,
                                     controla_estoque, estoque_minimo, vendivel, ativo, ordem, ncm, cest, cfop, csosn, cst_pis_cofins, gtin, criado_por)
                values (:novoId, :nome, :descricao, :categoria, :tipo, :setor, :unidade, :preco, :custo,
                        :controla, :minimo, :vendivel, :ativo, :ordem, :ncm, :cest, :cfop, :csosn, :cst, :gtin, :usr)
                """, new { novoId, p.nome, p.descricao, p.categoria, p.tipo, p.setor, p.unidade, p.preco, p.custo, p.controla, p.minimo,
                          p.vendivel, p.ativo, p.ordem, p.ncm, p.cest, p.cfop, p.csosn, p.cst, p.gtin, p.usr }, tx);
        }
        else
        {
            novoId = id.Value;
            await c.ExecuteAsync("""
                update produto set nome = :nome, descricao = :descricao, categoria_id = :categoria, tipo = :tipo, setor_producao_id = :setor,
                       unidade = :unidade, preco_venda = :preco, preco_custo = :custo, controla_estoque = :controla, estoque_minimo = :minimo,
                       vendivel = :vendivel, ativo = :ativo, ordem = :ordem, ncm = :ncm, cest = :cest, cfop = :cfop, csosn = :csosn,
                       cst_pis_cofins = :cst, gtin = :gtin, alterado_em = sysdate, alterado_por = :usr
                 where id = :id
                """, p, tx);
        }

        await c.ExecuteAsync("""
            insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
            values (:usr, 'PRODUTO', :pid, :acao, :detalhe)
            """, new { usr = usuarioId, pid = novoId.ToString(), acao = id is null ? "INSERT" : "UPDATE",
                      detalhe = new Clob(JsonSerializer.Serialize(new { antes, depois = p })) }, tx);
        tx.Commit();
        return Results.Ok(new { id = novoId, avisosFiscais = avisos });
    }
}
