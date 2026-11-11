using System.Data;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using Oracle.ManagedDataAccess.Client;

namespace JetPoa.Api.Endpoints;

/// <summary>Usuários identificados por PIN (operador do caixa e autorizador de descontos).</summary>
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

    public sealed record Identificacao(long UsuarioId, string Pin);
    public sealed record UsuarioInput(string Nome, string Login, long PerfilId, string? Pin, bool Ativo, Identificacao? Admin);

    /// <summary>Confere o PIN do usuário ativo. Retorna null se não conferir.</summary>
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

    public static void MapUsuarios(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Usuários");

        g.MapGet("/perfis", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Perfil>("select id, nome, limite_desconto_pct, autoriza_desconto from perfil where ativo = 'S' order by limite_desconto_pct desc");
        });

        g.MapGet("/usuarios", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<Usuario>("""
                select u.id, u.nome, u.login, u.perfil_id, p.nome perfil, p.limite_desconto_pct, p.autoriza_desconto, u.ativo
                  from usuario u join perfil p on p.id = u.perfil_id
                 order by u.ativo desc, u.nome
                """);
        });

        // Criação: o primeiro usuário (Administrador) não exige autorização; os demais exigem PIN de um administrador.
        g.MapPost("/usuarios", async (Db db, UsuarioInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var existe = await c.ExecuteScalarAsync<int>("select count(*) from usuario");
            if (existe > 0)
            {
                var erroAdmin = await ValidarAdminAsync(c, i.Admin);
                if (erroAdmin is not null) return erroAdmin;
            }
            else if (!await PerfilEhAdminAsync(c, i.PerfilId))
            {
                return Results.UnprocessableEntity(new { erro = "O primeiro usuário precisa ser Administrador." });
            }

            var erro = Validar(i, exigePin: true);
            if (erro is not null) return erro;
            var hash = Senha.Hash(i.Pin!);
            try
            {
                var id = await c.ExecuteScalarAsync<long>("select sq_usuario.nextval from dual");
                await c.ExecuteAsync("""
                    insert into usuario (id, nome, login, senha_hash, pin_hash, perfil_id, ativo)
                    values (:id, :nome, :login, :hash, :hash, :perfil_id, :ativo)
                    """, new { id, nome = i.Nome.Trim(), login = i.Login.Trim().ToLowerInvariant(), hash, perfil_id = i.PerfilId, ativo = i.Ativo ? "S" : "N" });
                return Results.Created($"/api/usuarios/{id}", new { id });
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = "Login já existe." });
            }
        });

        g.MapPut("/usuarios/{id:long}", async (Db db, long id, UsuarioInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var erroAdmin = await ValidarAdminAsync(c, i.Admin);
            if (erroAdmin is not null) return erroAdmin;
            var erro = Validar(i, exigePin: false);
            if (erro is not null) return erro;
            try
            {
                var n = await c.ExecuteAsync("""
                    update usuario
                       set nome = :nome, login = :login, perfil_id = :perfil_id, ativo = :ativo, alterado_em = sysdate,
                           pin_hash = nvl(:hash, pin_hash), senha_hash = nvl(:hash, senha_hash)
                     where id = :id
                    """, new { id, nome = i.Nome.Trim(), login = i.Login.Trim().ToLowerInvariant(), perfil_id = i.PerfilId,
                              ativo = i.Ativo ? "S" : "N", hash = string.IsNullOrEmpty(i.Pin) ? null : Senha.Hash(i.Pin) });
                return n == 0 ? Results.NotFound() : Results.NoContent();
            }
            catch (OracleException e) when (e.Number == 1)
            {
                return Results.Conflict(new { erro = "Login já existe." });
            }
        });

        // Confere um PIN (usado pela tela para validar antes de seguir)
        g.MapPost("/usuarios/conferir-pin", async (Db db, Identificacao i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var u = await AutenticarAsync(c, i.UsuarioId, i.Pin);
            return u is null ? Results.UnprocessableEntity(new { erro = "PIN incorreto." }) : Results.Ok(u);
        });
    }

    private static async Task<IResult?> ValidarAdminAsync(IDbConnection c, Identificacao? admin)
    {
        if (admin is null) return Results.UnprocessableEntity(new { erro = "Informe o PIN de um administrador." });
        var u = await AutenticarAsync(c, admin.UsuarioId, admin.Pin);
        if (u is null) return Results.UnprocessableEntity(new { erro = "PIN do administrador incorreto." });
        if (u.Perfil != "Administrador") return Results.UnprocessableEntity(new { erro = "Somente Administrador gerencia usuários." });
        return null;
    }

    private static async Task<bool> PerfilEhAdminAsync(IDbConnection c, long perfilId) =>
        await c.ExecuteScalarAsync<string?>("select nome from perfil where id = :perfilId", new { perfilId }) == "Administrador";

    private static IResult? Validar(UsuarioInput i, bool exigePin)
    {
        if (string.IsNullOrWhiteSpace(i.Nome)) return Results.UnprocessableEntity(new { erro = "Informe o nome." });
        if (string.IsNullOrWhiteSpace(i.Login) || i.Login.Trim().Length < 3) return Results.UnprocessableEntity(new { erro = "Login com pelo menos 3 caracteres." });
        if ((exigePin || !string.IsNullOrEmpty(i.Pin)) && !Senha.PinValido(i.Pin))
            return Results.UnprocessableEntity(new { erro = "PIN deve ter de 4 a 8 dígitos." });
        return null;
    }
}
