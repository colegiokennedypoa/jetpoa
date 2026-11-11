using System.Security.Claims;
using JetPoa.Api.Auth;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Endpoints;

public static class ClienteEndpoints
{
    public sealed class Cliente
    {
        public long Id { get; set; }
        public string? Cpf { get; set; }
        public string Nome { get; set; } = "";
        public string? Celular { get; set; }
        public string? Email { get; set; }
        public DateTime? DataNascimento { get; set; }
        public string? Sexo { get; set; }
        public long CategoriaId { get; set; }
        public string? Categoria { get; set; }
        public string AceitaComunicacao { get; set; } = "N";
        public string? Observacao { get; set; }
        public string Ativo { get; set; } = "S";
    }

    public sealed record ClienteInput(
        string? Cpf, string Nome, string? Celular, string? Email, DateTime? DataNascimento,
        string? Sexo, long? CategoriaId, bool AceitaComunicacao, string? Observacao, bool Ativo = true);

    public sealed class ClienteCategoria
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public decimal DescontoPctPadrao { get; set; }
        public string IsentaEstacionamento { get; set; } = "N";
        public string IsentaCouvert { get; set; } = "N";
        public string IsentaTaxaServico { get; set; } = "N";
        public decimal? LimiteComanda { get; set; }
    }

    private const string SelectCliente = """
        select c.id, c.cpf, c.nome, c.celular, c.email, c.data_nascimento, c.sexo,
               c.categoria_id, k.nome categoria, c.aceita_comunicacao, c.observacao, c.ativo
          from cliente c
          join cliente_categoria k on k.id = c.categoria_id
        """;

    public static void MapClientes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Clientes").RequireAuthorization(Permissoes.ClientesVer);
        var gEditar = g.MapGroup("").RequireAuthorization(Permissoes.ClientesEditar);

        g.MapGet("/cliente-categorias", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<ClienteCategoria>("""
                select id, nome, desconto_pct_padrao, isenta_estacionamento, isenta_couvert,
                       isenta_taxa_servico, limite_comanda
                  from cliente_categoria where ativo = 'S' order by id
                """);
        });

        // Busca por nome, CPF ou celular (paginada, 50 por página)
        g.MapGet("/clientes", async (Db db, string? busca, int? pagina, CancellationToken ct) =>
        {
            var p = Math.Max(pagina ?? 1, 1);
            var termo = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
            var digitos = Documento.SoDigitos(termo);
            using var c = await db.OpenAsync(ct);
            var sql = Db.Paginar(SelectCliente + """
                 where (:termo is null
                        or upper(c.nome) like '%' || upper(replace(:termo, ' ', '%')) || '%'
                        or (:digitos is not null and length(:digitos) >= 4
                            and (c.cpf like :digitos || '%' or c.celular like '%' || :digitos || '%')))
                 order by c.nome
                """);
            return await c.QueryAsync<Cliente>(sql, new
            {
                termo,
                digitos = string.IsNullOrEmpty(digitos) ? null : digitos,
                pg_ini = (p - 1) * 50,
                pg_fim = p * 50
            });
        });

        // Verificação de CPF já cadastrado (usada nas telas antes de salvar)
        g.MapGet("/clientes/cpf/{cpf}", async (Db db, string cpf, CancellationToken ct) =>
        {
            var d = Documento.SoDigitos(cpf);
            if (d is null || !Documento.CpfValido(d)) return Results.UnprocessableEntity(new { erro = "CPF inválido." });
            using var c = await db.OpenAsync(ct);
            var cli = await c.QuerySingleOrDefaultAsync<Cliente>(SelectCliente + " where c.cpf = :d", new { d });
            return cli is null ? Results.NotFound() : Results.Ok(cli);
        });

        g.MapGet("/clientes/{id:long}", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var cli = await c.QuerySingleOrDefaultAsync<Cliente>(SelectCliente + " where c.id = :id", new { id });
            return cli is null ? Results.NotFound() : Results.Ok(cli);
        });

        gEditar.MapPost("/clientes", async (Db db, ClienteInput input, CancellationToken ct) =>
        {
            var erros = Validar(input, out var cpf, out var celular);
            if (erros.Count > 0) return Results.ValidationProblem(erros);

            using var c = await db.OpenAsync(ct);
            if (await CpfEmUsoAsync(c, cpf, null) is { } dono) return dono;
            var categoriaId = input.CategoriaId
                ?? await c.ExecuteScalarAsync<long>("select id from cliente_categoria where nome = 'Normal'");
            var id = await c.ExecuteScalarAsync<long>("select sq_cliente.nextval from dual");
            try
            {
                await c.ExecuteAsync("""
                    insert into cliente (id, cpf, nome, celular, email, data_nascimento, sexo, categoria_id,
                                         aceita_comunicacao, consentimento_em, observacao, ativo, origem)
                    values (:id, :cpf, :nome, :celular, :email, :data_nascimento, :sexo, :categoria_id,
                            :aceita, case when :aceita = 'S' then sysdate end, :observacao, :ativo, 'CAIXA')
                    """, Parametros(id, input, cpf, celular, categoriaId));
            }
            catch (OracleException e) when (e.Number == 1) // ORA-00001 unique
            {
                return Results.Conflict(new { erro = "Já existe cliente com este CPF." });
            }
            return Results.Created($"/api/clientes/{id}", new { id });
        });

        gEditar.MapPut("/clientes/{id:long}", async (Db db, long id, ClienteInput input, CancellationToken ct) =>
        {
            var erros = Validar(input, out var cpf, out var celular);
            if (erros.Count > 0) return Results.ValidationProblem(erros);

            using var c = await db.OpenAsync(ct);
            if (await CpfEmUsoAsync(c, cpf, id) is { } dono) return dono;
            var categoriaId = input.CategoriaId
                ?? await c.ExecuteScalarAsync<long>("select categoria_id from cliente where id = :id", new { id });
            try
            {
                var n = await c.ExecuteAsync("""
                    update cliente
                       set cpf = :cpf, nome = :nome, celular = :celular, email = :email,
                           data_nascimento = :data_nascimento, sexo = :sexo, categoria_id = :categoria_id,
                           consentimento_em = case when :aceita = 'S' and aceita_comunicacao = 'N' then sysdate
                                                   when :aceita = 'N' then null else consentimento_em end,
                           aceita_comunicacao = :aceita, observacao = :observacao, ativo = :ativo,
                           alterado_em = sysdate
                     where id = :id
                    """, Parametros(id, input, cpf, celular, categoriaId));
                return n == 0 ? Results.NotFound() : Results.NoContent();
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = "Já existe cliente com este CPF." });
            }
        });
    }

    /// <summary>409 informando quem já usa o CPF (o índice único uk_cliente_cpf é a garantia final).</summary>
    private static async Task<IResult?> CpfEmUsoAsync(System.Data.IDbConnection c, string? cpf, long? idAtual)
    {
        if (cpf is null) return null;
        var dono = await c.QueryFirstOrDefaultAsync<Cliente>(
            SelectCliente + " where c.cpf = :cpf and (:idAtual is null or c.id <> :idAtual)", new { cpf, idAtual });
        return dono is null ? null
            : Results.Conflict(new { erro = $"CPF já cadastrado para {dono.Nome} (cliente nº {dono.Id}).", clienteId = dono.Id });
    }

    private static object Parametros(long id, ClienteInput i, string? cpf, string? celular, long categoriaId) => new
    {
        id,
        cpf,
        nome = i.Nome.Trim(),
        celular,
        email = string.IsNullOrWhiteSpace(i.Email) ? null : i.Email.Trim().ToLowerInvariant(),
        data_nascimento = i.DataNascimento?.Date,
        sexo = string.IsNullOrWhiteSpace(i.Sexo) ? null : i.Sexo.Trim().ToUpperInvariant(),
        categoria_id = categoriaId,
        aceita = i.AceitaComunicacao ? "S" : "N",
        observacao = string.IsNullOrWhiteSpace(i.Observacao) ? null : i.Observacao.Trim(),
        ativo = i.Ativo ? "S" : "N"
    };

    private static Dictionary<string, string[]> Validar(ClienteInput i, out string? cpf, out string? celular)
    {
        var erros = new Dictionary<string, string[]>();
        cpf = Documento.SoDigitos(i.Cpf);
        celular = Documento.SoDigitos(i.Celular);

        if (string.IsNullOrWhiteSpace(i.Nome) || i.Nome.Trim().Length < 2)
            erros["nome"] = ["Informe o nome do cliente."];
        if (cpf is not null && !Documento.CpfValido(cpf))
            erros["cpf"] = ["CPF inválido."];
        if (celular is not null && celular.Length is < 10 or > 13)
            erros["celular"] = ["Celular deve ter DDD + número."];
        if (i.Sexo is not null && i.Sexo.Trim() is not ("" or "M" or "F" or "O" or "m" or "f" or "o"))
            erros["sexo"] = ["Use M, F ou O."];
        if (cpf is null && celular is null)
            erros["contato"] = ["Informe ao menos CPF ou celular para identificar o cliente."];
        return erros;
    }
}
