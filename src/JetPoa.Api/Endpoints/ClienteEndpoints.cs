using System.Security.Claims;
using JetPoa.Api.Auth;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using System.Text.Json;
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
        public string IsentaEmbarque { get; set; } = "N";
        public string IsentaCouvert { get; set; } = "N";
        public string IsentaTaxaServico { get; set; } = "N";
        public decimal? LimiteComanda { get; set; }
        public string Ativo { get; set; } = "S";
        public int Clientes { get; set; }
    }

    public sealed record CategoriaInput(string Nome, decimal DescontoPctPadrao, bool IsentaEstacionamento, bool IsentaEmbarque,
        bool IsentaCouvert, bool IsentaTaxaServico, decimal? LimiteComanda, bool Ativo = true);

    /// <summary>Categoria usada por nome no cadastro automático (check-in, importação): não renomeia nem inativa.</summary>
    private const string CategoriaPadrao = "Normal";

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

        // ativas (cadastro de cliente); todas=true traz também as inativas e a contagem de clientes (tela Categorias)
        g.MapGet("/cliente-categorias", async (Db db, bool? todas, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<ClienteCategoria>("""
                select k.id, k.nome, k.desconto_pct_padrao, k.isenta_estacionamento, k.isenta_embarque, k.isenta_couvert,
                       k.isenta_taxa_servico, k.limite_comanda, k.ativo,
                       case when :todas = 1 then (select count(*) from cliente x where x.categoria_id = k.id) else 0 end clientes
                  from cliente_categoria k where (:todas = 1 or k.ativo = 'S') order by k.ativo desc, k.id
                """, new { todas = todas == true ? 1 : 0 });
        });

        var gCat = app.MapGroup("/api/cliente-categorias").WithTags("Clientes").RequireAuthorization(Permissoes.ClientesCategorias);
        gCat.MapPost("", (ClaimsPrincipal user, Db db, CategoriaInput i, CancellationToken ct) => SalvarCategoriaAsync(user, db, null, i, ct));
        gCat.MapPut("/{id:long}", (ClaimsPrincipal user, Db db, long id, CategoriaInput i, CancellationToken ct) => SalvarCategoriaAsync(user, db, id, i, ct));

        // Busca por nome, CPF ou celular (paginada, 50 por página).
        // Sem acesso à tela Clientes (garçom, check-in): só busca com 3+ caracteres e 1ª página — nada de listar o cadastro.
        g.MapGet("/clientes", async (ClaimsPrincipal user, Db db, string? busca, int? pagina, CancellationToken ct) =>
        {
            var p = Math.Max(pagina ?? 1, 1);
            var termo = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
            if (!user.Pode(Permissoes.ClientesCadastro) && (termo is null || termo.Length < 3 || p > 1))
                return Results.Ok(Array.Empty<Cliente>());
            var digitos = Documento.SoDigitos(termo);
            using var c = await db.OpenAsync(ct);
            var sql = Db.Paginar(SelectCliente + """
                 where (:termo is null
                        or upper(c.nome) like '%' || upper(replace(:termo, ' ', '%')) || '%'
                        or (:digitos is not null and length(:digitos) >= 4
                            and (c.cpf like :digitos || '%' or c.celular like '%' || :digitos || '%')))
                 order by c.nome
                """);
            return Results.Ok(await c.QueryAsync<Cliente>(sql, new
            {
                termo,
                digitos = string.IsNullOrEmpty(digitos) ? null : digitos,
                pg_ini = (p - 1) * 50,
                pg_fim = p * 50
            }));
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
        }).RequireAuthorization(Permissoes.ClientesCadastro);   // editar existente: só quem tem a tela Clientes
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

    private static async Task<IResult> SalvarCategoriaAsync(ClaimsPrincipal user, Db db, long? id, CategoriaInput i, CancellationToken ct)
    {
        static IResult Erro(string m) => Results.UnprocessableEntity(new { erro = m });
        var nome = i.Nome?.Trim();
        if (string.IsNullOrEmpty(nome) || nome.Length > 60) return Erro("Informe o nome da categoria (até 60 caracteres).");
        if (i.DescontoPctPadrao is < 0 or > 100) return Erro("Desconto padrão deve estar entre 0 e 100%.");
        if (i.LimiteComanda is <= 0 or > 9999999) return Erro("Limite da comanda deve ser maior que zero (vazio = limite geral).");
        var depois = new
        {
            nome,
            desc = Math.Round(i.DescontoPctPadrao, 2),
            est = i.IsentaEstacionamento ? "S" : "N",
            emb = i.IsentaEmbarque ? "S" : "N",
            cou = i.IsentaCouvert ? "S" : "N",
            ser = i.IsentaTaxaServico ? "S" : "N",
            lim = i.LimiteComanda is { } l ? Math.Round(l, 2) : (decimal?)null,
            ativo = i.Ativo ? "S" : "N",
        };

        using var c = await db.OpenAsync(ct);
        using var tx = c.BeginTransaction();
        ClienteCategoria? antes = null;
        if (id is not null)
        {
            antes = await c.QuerySingleOrDefaultAsync<ClienteCategoria>("""
                select id, nome, desconto_pct_padrao, isenta_estacionamento, isenta_embarque, isenta_couvert,
                       isenta_taxa_servico, limite_comanda, ativo
                  from cliente_categoria where id = :id for update
                """, new { id }, tx);
            if (antes is null) return Results.NotFound(new { erro = "Categoria não encontrada." });
            if (antes.Nome == CategoriaPadrao && (nome != CategoriaPadrao || !i.Ativo))
                return Erro($"A categoria {CategoriaPadrao} é a padrão dos clientes novos: não pode ser renomeada nem inativada.");
            if (antes.Ativo == "S" && !i.Ativo)
            {
                var qtd = await c.ExecuteScalarAsync<int>("select count(*) from cliente where categoria_id = :id", new { id }, tx);
                if (qtd > 0) return Erro($"{qtd} cliente(s) estão nesta categoria: mude a categoria deles antes de inativar.");
            }
        }
        try
        {
            if (id is null)
            {
                await c.ExecuteAsync("""
                    insert into cliente_categoria (nome, desconto_pct_padrao, isenta_estacionamento, isenta_embarque, isenta_couvert,
                                                   isenta_taxa_servico, limite_comanda, ativo)
                    values (:nome, :desc, :est, :emb, :cou, :ser, :lim, :ativo)
                    """, depois, tx);
                id = await c.ExecuteScalarAsync<long>("select id from cliente_categoria where nome = :nome", new { nome }, tx);
            }
            else
                await c.ExecuteAsync("""
                    update cliente_categoria set nome = :nome, desconto_pct_padrao = :desc, isenta_estacionamento = :est,
                           isenta_embarque = :emb, isenta_couvert = :cou, isenta_taxa_servico = :ser, limite_comanda = :lim, ativo = :ativo
                     where id = :id
                    """, new { id, depois.nome, depois.desc, depois.est, depois.emb, depois.cou, depois.ser, depois.lim, depois.ativo }, tx);
        }
        catch (OracleException e) when (e.Number == 1)
        {
            return Results.Conflict(new { erro = $"Já existe uma categoria chamada {nome}." });
        }
        // muda descontos e isenções automáticas do fechamento: fica registrado quem mudou e o que era antes
        await c.ExecuteAsync("""
            insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
            values (:usr, 'CLIENTE_CATEGORIA', :eid, :acao, :det)
            """, new { usr = user.Id(), eid = id.ToString(), acao = antes is null ? "INCLUIR" : "ALTERAR",
                       det = JsonSerializer.Serialize(new { antes, depois }) }, tx);
        tx.Commit();
        return Results.NoContent();
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
