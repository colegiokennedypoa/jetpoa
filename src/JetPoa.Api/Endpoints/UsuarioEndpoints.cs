using System.Data;
using System.Security.Claims;
using Dapper;
using JetPoa.Api.Auth;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Endpoints;

/// <summary>
/// Cadastro de usuários. Cada pessoa entra com login + senha; o PIN é opcional e serve só para
/// AUTORIZAR no caixa de outra pessoa (gerente liberando desconto acima do limite, consumo interno, cancelamento).
/// </summary>
public static class UsuarioEndpoints
{
    public sealed class Perfil
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public decimal LimiteDescontoPct { get; set; }
        public string AutorizaDesconto { get; set; } = "N";
    }

    public sealed class Usuario
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Login { get; set; } = "";
        public long PerfilId { get; set; }
        public string Perfil { get; set; } = "";
        public decimal LimiteDescontoPct { get; set; }
        public string AutorizaDesconto { get; set; } = "N";
        public string Ativo { get; set; } = "S";
        public string TemPin { get; set; } = "N";
        public string PrecisaTrocarSenha { get; set; } = "N";
        public DateTime? UltimoLogin { get; set; }
    }

    private sealed class UsuarioPin
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Perfil { get; set; } = "";
        public decimal LimiteDescontoPct { get; set; }
        public string AutorizaDesconto { get; set; } = "N";
        public string? PinHash { get; set; }
    }

    /// <param name="Senha">Obrigatória ao criar (provisória: troca no 1º login). Na edição, preencher = redefinir.</param>
    /// <param name="Pin">Opcional: 4 a 8 dígitos, para autorizar no caixa de outra pessoa.</param>
    public sealed record UsuarioInput(string Nome, string Login, long PerfilId, string? Senha, string? Pin, bool Ativo);

    private const string SelectUsuario = """
        select u.id, u.nome, u.login, u.perfil_id, p.nome perfil, p.limite_desconto_pct, p.autoriza_desconto, u.ativo,
               case when u.pin_hash is not null then 'S' else 'N' end tem_pin, u.precisa_trocar_senha, u.ultimo_login
          from usuario u join perfil p on p.id = u.perfil_id
        """;

    /// <summary>Confere o PIN de um usuário ativo (autorização no caixa). Retorna null se não conferir.</summary>
    public static async Task<Usuario?> AutenticarAsync(IDbConnection c, long usuarioId, string? pin, IDbTransaction? tx = null)
    {
        var u = await c.QuerySingleOrDefaultAsync<UsuarioPin>("""
            select u.id, u.nome, p.nome perfil, p.limite_desconto_pct, p.autoriza_desconto, u.pin_hash
              from usuario u join perfil p on p.id = u.perfil_id
             where u.id = :usuarioId and u.ativo = 'S'
            """, new { usuarioId }, tx);
        if (u is null || !Senha.Verificar(pin ?? "", u.PinHash)) return null;
        return new Usuario { Id = u.Id, Nome = u.Nome, Perfil = u.Perfil, LimiteDescontoPct = u.LimiteDescontoPct, AutorizaDesconto = u.AutorizaDesconto };
    }

    /// <summary>Usuário ativo pelo id (o logado), com limites do perfil.</summary>
    public static async Task<Usuario?> ObterAsync(IDbConnection c, long id, IDbTransaction? tx = null) =>
        await c.QuerySingleOrDefaultAsync<Usuario>(SelectUsuario + " where u.id = :id and u.ativo = 'S'", new { id }, tx);

    public static void MapUsuarios(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Usuários");

        g.MapGet("/perfis", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Perfil>("select id, nome, limite_desconto_pct, autoriza_desconto from perfil where ativo = 'S' order by limite_desconto_pct desc");
        }).AllowAnonymous();   // usado também no cadastro do 1º Administrador (sem login)

        // Lista completa só para quem gerencia usuários
        g.MapGet("/usuarios", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Usuario>(SelectUsuario + " order by u.ativo desc, u.nome");
        }).RequireAuthorization(Permissoes.UsuariosGerenciar);

        // Quem pode autorizar no caixa (gerentes/admins ativos com PIN) — usado pelo caixa/estoque
        g.MapGet("/usuarios/autorizadores", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Usuario>(SelectUsuario + " where u.ativo = 'S' and p.autoriza_desconto = 'S' and u.pin_hash is not null order by u.nome");
        });

        // Criação: sem nenhum usuário cadastrado, cria o 1º Administrador sem login; depois, só quem gerencia usuários.
        g.MapPost("/usuarios", async (ClaimsPrincipal user, Db db, UsuarioInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var existe = await c.ExecuteScalarAsync<int>("select count(*) from usuario");
            if (existe > 0 && !user.Pode(Permissoes.UsuariosGerenciar))
                return user.Identity?.IsAuthenticated == true ? Results.Forbid() : Results.Unauthorized();
            if (existe == 0 && !await PerfilEhAdminAsync(c, i.PerfilId))
                return Results.UnprocessableEntity(new { erro = "O primeiro usuário precisa ser Administrador." });

            if (Validar(i, criando: true) is { } erro) return erro;
            try
            {
                var id = await c.ExecuteScalarAsync<long>("select sq_usuario.nextval from dual");
                await c.ExecuteAsync("""
                    insert into usuario (id, nome, login, senha_hash, pin_hash, perfil_id, ativo, precisa_trocar_senha, carimbo_seguranca)
                    values (:id, :nome, :login, :senha, :pin, :perfil_id, :ativo, :trocar, :carimbo)
                    """, new
                {
                    id, nome = i.Nome.Trim(), login = i.Login.Trim().ToLowerInvariant(), senha = Senha.Hash(i.Senha!),
                    pin = string.IsNullOrEmpty(i.Pin) ? null : Senha.Hash(i.Pin), perfil_id = i.PerfilId, ativo = i.Ativo ? "S" : "N",
                    trocar = existe == 0 ? "N" : "S",   // o 1º administrador escolhe a própria senha
                    carimbo = Guid.NewGuid().ToString("N"),
                });
                return Results.Created($"/api/usuarios/{id}", new { id });
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = "Login já existe." });
            }
        }).AllowAnonymous();

        g.MapPut("/usuarios/{id:long}", async (ClaimsPrincipal user, Db db, IMemoryCache cache, long id, UsuarioInput i, CancellationToken ct) =>
        {
            if (Validar(i, criando: false) is { } erro) return erro;
            if (id == user.Id() && !i.Ativo) return Results.UnprocessableEntity(new { erro = "Você não pode desativar o próprio usuário." });
            using var c = await db.OpenAsync(ct);
            var antes = await c.QuerySingleOrDefaultAsync<Usuario>(SelectUsuario + " where u.id = :id", new { id });
            if (antes is null) return Results.NotFound();
            if (antes.Perfil == "Administrador" && (await PerfilEhAdminAsync(c, i.PerfilId) == false || !i.Ativo)
                && await c.ExecuteScalarAsync<int>("""
                    select count(*) from usuario u join perfil p on p.id = u.perfil_id where p.nome = 'Administrador' and u.ativo = 'S'
                    """) <= 1)
                return Results.UnprocessableEntity(new { erro = "É preciso manter pelo menos um Administrador ativo." });

            // trocar senha, perfil ou desativar derruba as sessões abertas desse usuário
            var derrubar = !string.IsNullOrEmpty(i.Senha) || antes.PerfilId != i.PerfilId || (antes.Ativo == "S") != i.Ativo;
            try
            {
                await c.ExecuteAsync("""
                    update usuario
                       set nome = :nome, login = :login, perfil_id = :perfil_id, ativo = :ativo, alterado_em = sysdate,
                           senha_hash = nvl(:senha, senha_hash),
                           precisa_trocar_senha = case when :senha is not null then 'S' else precisa_trocar_senha end,
                           pin_hash = nvl(:pin, pin_hash),
                           tentativas_falhas = case when :senha is not null then 0 else tentativas_falhas end,
                           bloqueado_ate = case when :senha is not null then null else bloqueado_ate end,
                           carimbo_seguranca = case when :derrubar = 1 then :carimbo else carimbo_seguranca end
                     where id = :id
                    """, new
                {
                    id, nome = i.Nome.Trim(), login = i.Login.Trim().ToLowerInvariant(), perfil_id = i.PerfilId, ativo = i.Ativo ? "S" : "N",
                    senha = string.IsNullOrEmpty(i.Senha) ? null : Senha.Hash(i.Senha),
                    pin = string.IsNullOrEmpty(i.Pin) ? null : Senha.Hash(i.Pin),
                    derrubar = derrubar ? 1 : 0, carimbo = Guid.NewGuid().ToString("N"),
                });
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = "Login já existe." });
            }
            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:usr, 'USUARIO', :eid, 'UPDATE', :det)
                """, new { usr = user.Id(), eid = id.ToString(),
                          det = $"{{\"perfilAntes\":{antes.PerfilId},\"perfil\":{i.PerfilId},\"ativo\":\"{(i.Ativo ? "S" : "N")}\",\"senhaRedefinida\":{(!string.IsNullOrEmpty(i.Senha)).ToString().ToLowerInvariant()}}}" });
            cache.Remove(AuthEndpoints.ChaveCache(id));
            return Results.NoContent();
        }).RequireAuthorization(Permissoes.UsuariosGerenciar);

        // Confere um PIN de autorizador (usado pela tela antes de enviar)
        g.MapPost("/usuarios/conferir-pin", async (Db db, ConferirPinInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var u = await AutenticarAsync(c, i.UsuarioId, i.Pin);
            return u is null ? Results.UnprocessableEntity(new { erro = "PIN incorreto." }) : Results.Ok(u);
        });
    }

    public sealed record ConferirPinInput(long UsuarioId, string Pin);

    private static async Task<bool> PerfilEhAdminAsync(IDbConnection c, long perfilId) =>
        await c.ExecuteScalarAsync<string?>("select nome from perfil where id = :perfilId", new { perfilId }) == "Administrador";

    private static IResult? Validar(UsuarioInput i, bool criando)
    {
        if (string.IsNullOrWhiteSpace(i.Nome)) return Results.UnprocessableEntity(new { erro = "Informe o nome." });
        if (string.IsNullOrWhiteSpace(i.Login) || i.Login.Trim().Length < 3 || i.Login.Contains(' '))
            return Results.UnprocessableEntity(new { erro = "Login com pelo menos 3 caracteres, sem espaços." });
        if ((criando || !string.IsNullOrEmpty(i.Senha)) && AuthEndpoints.ValidarSenha(i.Senha) is { } s)
            return Results.UnprocessableEntity(new { erro = s });
        if (!string.IsNullOrEmpty(i.Pin) && !Senha.PinValido(i.Pin))
            return Results.UnprocessableEntity(new { erro = "PIN deve ter de 4 a 8 dígitos." });
        return null;
    }
}
