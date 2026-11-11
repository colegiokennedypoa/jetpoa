using System.Security.Claims;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace JetPoa.Api.Auth;

/// <summary>Login com usuário e senha (cookie de sessão), troca de senha e dados do usuário logado.</summary>
public static class AuthEndpoints
{
    public const int SenhaMinima = 6;
    private const int MaxFalhas = 5;
    private static readonly TimeSpan Bloqueio = TimeSpan.FromMinutes(15);

    public sealed record LoginInput(string Login, string Senha);
    public sealed record TrocarSenhaInput(string Atual, string Nova);

    public sealed class UsuarioLogin
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string Login { get; set; } = "";
        public string Perfil { get; set; } = "";
        public string Ativo { get; set; } = "S";
        public string SenhaHash { get; set; } = "";
        public string CarimboSeguranca { get; set; } = "";
        public string PrecisaTrocarSenha { get; set; } = "S";
        public decimal LimiteDescontoPct { get; set; }
        public string AutorizaDesconto { get; set; } = "N";
        public int TentativasFalhas { get; set; }
        public DateTime? BloqueadoAte { get; set; }
        public long? GarcomId { get; set; }
        public string? GarcomNome { get; set; }
    }

    private const string SelectUsuario = """
        select u.id, u.nome, u.login, p.nome perfil, u.ativo, u.senha_hash, u.carimbo_seguranca, u.precisa_trocar_senha,
               p.limite_desconto_pct, p.autoriza_desconto, u.tentativas_falhas, u.bloqueado_ate,
               u.garcom_id, g.nome garcom_nome
          from usuario u join perfil p on p.id = u.perfil_id
          left join garcom g on g.id = u.garcom_id
        """;

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Login");

        // Indica se ainda não existe usuário (tela mostra o cadastro do primeiro Administrador)
        g.MapGet("/primeiro-acesso", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return new { semUsuarios = await c.ExecuteScalarAsync<int>("select count(*) from usuario") == 0 };
        }).AllowAnonymous();

        g.MapPost("/login", async (HttpContext http, Db db, LoginInput i, CancellationToken ct) =>
        {
            var login = (i.Login ?? "").Trim().ToLowerInvariant();
            using var c = await db.OpenAsync(ct);
            var u = await c.QuerySingleOrDefaultAsync<UsuarioLogin>(SelectUsuario + " where u.login = :login", new { login });
            const string invalido = "Usuário ou senha incorretos.";
            if (u is null || u.Ativo != "S") { await Task.Delay(400, ct); return Results.UnprocessableEntity(new { erro = invalido }); }
            if (u.BloqueadoAte > DateTime.Now)
                return Results.UnprocessableEntity(new { erro = $"Usuário bloqueado por excesso de tentativas até {u.BloqueadoAte:HH:mm}." });

            if (!Senha.Verificar(i.Senha ?? "", u.SenhaHash))
            {
                var falhas = u.TentativasFalhas + 1;
                await c.ExecuteAsync("""
                    update usuario set tentativas_falhas = case when :falhas >= :max then 0 else :falhas end,
                           bloqueado_ate = case when :falhas >= :max then sysdate + :min / 1440 else bloqueado_ate end
                     where id = :id
                    """, new { id = u.Id, falhas, max = MaxFalhas, min = Bloqueio.TotalMinutes });
                return Results.UnprocessableEntity(new { erro = falhas >= MaxFalhas
                    ? $"Usuário bloqueado por {Bloqueio.TotalMinutes:0} minutos (muitas tentativas)." : invalido });
            }

            await c.ExecuteAsync("update usuario set tentativas_falhas = 0, bloqueado_ate = null, ultimo_login = sysdate where id = :id", new { id = u.Id });
            await EntrarAsync(http, u);
            return Results.Ok(Eu(u));
        }).AllowAnonymous();

        g.MapPost("/sair", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        g.MapGet("/eu", async (ClaimsPrincipal user, Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var u = await c.QuerySingleOrDefaultAsync<UsuarioLogin>(SelectUsuario + " where u.id = :id", new { id = user.Id() });
            return u is null ? Results.Unauthorized() : Results.Ok(Eu(u));
        }).RequireAuthorization();

        g.MapPost("/trocar-senha", async (HttpContext http, ClaimsPrincipal user, Db db, IMemoryCache cache, TrocarSenhaInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var u = await c.QuerySingleAsync<UsuarioLogin>(SelectUsuario + " where u.id = :id", new { id = user.Id() });
            if (!Senha.Verificar(i.Atual ?? "", u.SenhaHash)) return Results.UnprocessableEntity(new { erro = "Senha atual incorreta." });
            if (ValidarSenha(i.Nova) is { } erro) return Results.UnprocessableEntity(new { erro });
            if (i.Nova == i.Atual) return Results.UnprocessableEntity(new { erro = "A nova senha deve ser diferente da atual." });
            u.CarimboSeguranca = Guid.NewGuid().ToString("N");
            u.PrecisaTrocarSenha = "N";
            await c.ExecuteAsync("""
                update usuario set senha_hash = :hash, precisa_trocar_senha = 'N', carimbo_seguranca = :carimbo, alterado_em = sysdate
                 where id = :id
                """, new { id = u.Id, hash = Senha.Hash(i.Nova), carimbo = u.CarimboSeguranca });
            cache.Remove(ChaveCache(u.Id));
            await EntrarAsync(http, u);   // renova o cookie com o carimbo novo (outras sessões caem)
            return Results.Ok(Eu(u));
        }).RequireAuthorization();
    }

    public static string? ValidarSenha(string? s) =>
        string.IsNullOrEmpty(s) || s.Length < SenhaMinima ? $"A senha deve ter pelo menos {SenhaMinima} caracteres."
        : s.Length > 100 ? "Senha muito longa." : null;

    private static object Eu(UsuarioLogin u) => new
    {
        u.Id, u.Nome, u.Login, u.Perfil, u.LimiteDescontoPct, autorizaDesconto = u.AutorizaDesconto == "S",
        precisaTrocarSenha = u.PrecisaTrocarSenha == "S", permissoes = Permissoes.DoPerfil(u.Perfil),
        u.GarcomId, u.GarcomNome,
    };

    private static async Task EntrarAsync(HttpContext http, UsuarioLogin u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new("perfil", u.Perfil),
            new("carimbo", u.CarimboSeguranca),
            new("trocar", u.PrecisaTrocarSenha),
        };
        claims.AddRange(Permissoes.DoPerfil(u.Perfil).Select(p => new Claim("perm", p)));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }

    public static string ChaveCache(long id) => $"usuario-carimbo-{id}";

    /// <summary>
    /// A cada requisição confere (com cache de 30 s) se o usuário continua ativo e com o mesmo carimbo:
    /// trocar senha, perfil ou desativar derruba as sessões abertas.
    /// </summary>
    public static async Task ValidarSessaoAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (principal?.FindFirstValue(ClaimTypes.NameIdentifier) is not { } idTxt) { ctx.RejectPrincipal(); return; }
        var id = long.Parse(idTxt);
        var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        var carimbo = await cache.GetOrCreateAsync(ChaveCache(id), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<Db>();
            using var c = await db.OpenAsync();
            return await c.ExecuteScalarAsync<string?>(
                "select carimbo_seguranca from usuario where id = :id and ativo = 'S'", new { id });
        });
        if (carimbo is null || carimbo != principal.FindFirstValue("carimbo"))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
