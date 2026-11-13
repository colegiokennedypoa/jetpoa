using Dapper;
using JetPoa.Api.Auth;
using Oracle.ManagedDataAccess.Client;
using JetPoa.Api.Data;

namespace JetPoa.Api.Endpoints;

public static class CatalogoEndpoints
{
    public sealed class Categoria
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public int Ordem { get; set; }
        public string Ativo { get; set; } = "S";
    }

    public sealed class SetorProducao
    {
        public long Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Imprime { get; set; } = "S";
        public string? ImpressoraHost { get; set; }
        public int? ImpressoraPorta { get; set; }
        public int ColunasImpressao { get; set; } = 48;
        public string Protocolo { get; set; } = "ESCPOS";
    }

    public sealed class ProdutoResumo
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Tipo { get; set; } = "P";
        public decimal PrecoVenda { get; set; }
        public decimal EstoqueAtual { get; set; }
        public decimal? EstoqueMinimo { get; set; }
        public string Ativo { get; set; } = "S";
        public long CategoriaId { get; set; }
        public string Categoria { get; set; } = "";
        public string Setor { get; set; } = "";
        public string? Ncm { get; set; }
        public string? Cfop { get; set; }
        public string? Csosn { get; set; }
        public string? CstPisCofins { get; set; }
        /// <summary>S dados fiscais completos · N pendente · X serviço/taxa (fora da NFC-e).</summary>
        public string FiscalOk { get; set; } = "N";
    }

    public sealed class Garcom
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Ativo { get; set; } = "S";
    }

    public sealed class Area
    {
        public long Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Ativo { get; set; } = "S";
    }

    public sealed record AreaInput(string? Codigo, string? Nome, bool Ativo);

    public sealed class Componente
    {
        public long ComponenteId { get; set; }
        public string Nome { get; set; } = "";
        public decimal Quantidade { get; set; }
    }

    public static void MapCatalogo(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Catálogo");

        g.MapGet("/categorias", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Categoria>(
                "select id, nome, ordem, ativo from categoria order by ordem, nome");
        });

        g.MapGet("/setores", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<SetorProducao>(
                "select id, codigo, nome, imprime, impressora_host, impressora_porta, colunas_impressao, protocolo from setor_producao where ativo = 'S' order by id");
        });

        g.MapGet("/produtos", async (Db db, string? busca, long? categoriaId, bool? somenteAtivos, bool? pendenciaFiscal, bool? somenteVendiveis, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            const string sql = """
                select p.id, p.nome, p.tipo, p.preco_venda, p.estoque_atual, p.estoque_minimo, p.ativo,
                       p.categoria_id, c.nome categoria, s.codigo setor,
                       p.ncm, p.cfop, p.csosn, p.cst_pis_cofins,
                       case when p.tipo = 'S' or p.taxa_tipo is not null then 'X'   -- serviço/taxa: fora da NFC-e
                            when p.ncm is not null and p.cfop is not null and p.csosn is not null
                                 and p.cst_pis_cofins is not null then 'S' else 'N' end fiscal_ok
                  from produto p
                  join categoria c on c.id = p.categoria_id
                  join setor_producao s on s.id = p.setor_producao_id
                 where (:busca is null or upper(p.nome) like '%' || upper(replace(:busca, ' ', '%')) || '%')
                   and (:categoria_id is null or p.categoria_id = :categoria_id)
                   and (:somente_ativos = 0 or p.ativo = 'S')
                   and (:somente_vendiveis = 0 or p.vendivel = 'S')
                   and (:pendencia_fiscal = 0 or (p.tipo <> 'S' and p.taxa_tipo is null
                        and (p.ncm is null or p.cfop is null or p.csosn is null or p.cst_pis_cofins is null)))
                 order by c.ordem, p.ordem, p.nome
                """;
            return await c.QueryAsync<ProdutoResumo>(sql, new
            {
                busca = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim(),
                categoria_id = categoriaId,
                somente_ativos = somenteAtivos == false ? 0 : 1,
                somente_vendiveis = somenteVendiveis == true ? 1 : 0,
                pendencia_fiscal = pendenciaFiscal == true ? 1 : 0
            });
        });

        g.MapGet("/produtos/{id:long}/componentes", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Componente>("""
                select pc.componente_id, p.nome, pc.quantidade
                  from produto_componente pc
                  join produto p on p.id = pc.componente_id
                 where pc.produto_id = :id
                 order by p.nome
                """, new { id });
        });

        g.MapGet("/garcons", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Garcom>("select id, nome, ativo from garcom order by nome");
        });

        // áreas ativas (check-in, caixa); todas=true traz também as inativas (cadastro)
        g.MapGet("/areas", async (Db db, bool? todas, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Area>(
                "select id, codigo, nome, ativo from area where (:todas = 1 or ativo = 'S') order by ativo desc, codigo",
                new { todas = todas == true ? 1 : 0 });
        });

        var areas = app.MapGroup("/api/areas").WithTags("Catálogo").RequireAuthorization(Permissoes.EventoEditar);
        areas.MapPost("", async (Db db, AreaInput i, CancellationToken ct) => await SalvarAreaAsync(db, null, i, ct));
        areas.MapPut("/{id:long}", async (Db db, long id, AreaInput i, CancellationToken ct) => await SalvarAreaAsync(db, id, i, ct));
    }

    private static async Task<IResult> SalvarAreaAsync(Db db, long? id, AreaInput i, CancellationToken ct)
    {
        var codigo = i.Codigo?.Trim().ToUpperInvariant();
        var nome = i.Nome?.Trim();
        if (string.IsNullOrEmpty(codigo) || codigo.Length > 10) return Results.UnprocessableEntity(new { erro = "Informe o código da área (até 10 caracteres)." });
        if (string.IsNullOrEmpty(nome) || nome.Length > 100) return Results.UnprocessableEntity(new { erro = "Informe o nome da área (até 100 caracteres)." });
        var ativo = i.Ativo ? "S" : "N";
        using var c = await db.OpenAsync(ct);
        try
        {
            if (id is null)
                await c.ExecuteAsync("insert into area (codigo, nome, ativo) values (:codigo, :nome, :ativo)", new { codigo, nome, ativo });
            else if (await c.ExecuteAsync("update area set codigo = :codigo, nome = :nome, ativo = :ativo where id = :id", new { id, codigo, nome, ativo }) == 0)
                return Results.NotFound(new { erro = "Área não encontrada." });
        }
        catch (OracleException e) when (e.Number == 1)
        {
            return Results.Conflict(new { erro = $"Já existe uma área com o código {codigo}." });
        }
        return Results.NoContent();
    }
}
