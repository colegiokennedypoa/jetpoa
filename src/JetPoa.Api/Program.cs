using System.Globalization;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Endpoints;
using JetPoa.Api.Impressao;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");

// Colunas Oracle em snake_case (PRECO_VENDA) -> propriedades C# (PrecoVenda)
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<ImpressaoProducao>();
builder.Services.Configure<JetPoa.Api.Fiscal.NfceOptions>(builder.Configuration.GetSection("Nfce"));
builder.Services.AddSingleton<JetPoa.Api.Fiscal.NfceService>();
builder.Services.AddHostedService<JetPoa.Api.Fiscal.NfceWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImpressaoProducao>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.CustomSchemaIds(t => t.FullName!.Replace('+', '.')));
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

// Front-end compilado (web/dist copiado para wwwroot no publish)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (Db db, CancellationToken ct) =>
{
    using var c = await db.OpenAsync(ct);
    var agora = await c.ExecuteScalarAsync<DateTime>("select sysdate from dual");
    return Results.Ok(new { status = "ok", banco = ((Oracle.ManagedDataAccess.Client.OracleConnection)c).ServerVersion, agora });
}).WithTags("Sistema");

app.MapCatalogo();
app.MapClientes();
app.MapComandas();
app.MapProducao();
app.MapUsuarios();
app.MapFechamento();
app.MapFiscal();

app.MapFallbackToFile("index.html");

app.Run();
