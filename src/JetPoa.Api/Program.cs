using System.Globalization;
using Dapper;
using JetPoa.Api.Auth;
using JetPoa.Api.Data;
using JetPoa.Api.Endpoints;
using JetPoa.Api.Impressao;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");

// Colunas Oracle em snake_case (PRECO_VENDA) -> propriedades C# (PrecoVenda)
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // como serviço do Windows o diretório atual é System32: usa a pasta do executável
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});
builder.Host.UseWindowsService(o => o.ServiceName = "JetPOABistro");

// Segredos locais (fora do Git): conexão, certificado A1 e CSC
builder.Configuration.AddArquivoConf(builder.Configuration["ArquivoConfiguracao"]);
// variáveis de ambiente continuam valendo por cima do arquivo (ex.: desligar algo pontualmente)
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<ImpressaoProducao>();
builder.Services.Configure<JetPoa.Api.Fiscal.NfceOptions>(builder.Configuration.GetSection("Nfce"));
builder.Services.AddSingleton<JetPoa.Api.Fiscal.NfceService>();
builder.Services.AddHostedService<JetPoa.Api.Fiscal.NfceWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImpressaoProducao>());
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.CustomSchemaIds(t => t.FullName!.Replace('+', '.')));
builder.Services.AddProblemDetails();

// ---- Login (cookie de sessão) e permissões por perfil ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "jetpoa.sessao";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;          // protege contra requisições de outros sites
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);        // um turno; renova enquanto usado
        o.SlidingExpiration = true;
        // API: responde 401/403 em vez de redirecionar para página de login
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = AuthEndpoints.ValidarSessaoAsync;
    });
builder.Services.AddAuthorization(o =>
{
    foreach (var p in Permissoes.Todas) o.AddPolicy(p, pol => pol.RequireClaim("perm", p));
    // toda rota exige login, salvo as marcadas com AllowAnonymous
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

// Front-end compilado (web/dist copiado para wwwroot no publish) — a tela de login precisa carregar sem sessão
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Primeiro acesso com senha provisória: só deixa usar as rotas de login até trocar a senha
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirst("trocar")?.Value == "S"
        && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/auth"))
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { erro = "Troque a senha provisória para continuar." });
        return;
    }
    await next();
});

app.MapGet("/api/health", async (Db db, CancellationToken ct) =>
{
    using var c = await db.OpenAsync(ct);
    var agora = await c.ExecuteScalarAsync<DateTime>("select sysdate from dual");
    return Results.Ok(new { status = "ok", banco = ((Oracle.ManagedDataAccess.Client.OracleConnection)c).ServerVersion, agora });
}).WithTags("Sistema").AllowAnonymous();

app.MapAuth();
app.MapCatalogo();
app.MapClientes();
app.MapComandas();
app.MapProducao();
app.MapUsuarios();
app.MapFechamento();
app.MapFiscal();
app.MapCheckin();
app.MapEstoque();
app.MapRelatorios();
app.MapProdutos();
app.MapImplantacao();

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
