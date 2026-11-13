using System.Security.Claims;
using JetPoa.Api.Auth;
using System.Text;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using JetPoa.Api.Fiscal;
using JetPoa.Api.Impressao;
using Microsoft.Extensions.Options;

namespace JetPoa.Api.Endpoints;

public static class FiscalEndpoints
{
    public sealed class NotaResumo
    {
        public long Id { get; set; }
        public long? ComandaId { get; set; }
        public int? ComandaNumero { get; set; }
        public int Serie { get; set; }
        public int Numero { get; set; }
        public string Chave { get; set; } = "";
        public string Ambiente { get; set; } = "";
        public string TpEmis { get; set; } = "";
        public string Situacao { get; set; } = "";
        public DateTime EmitidaEm { get; set; }
        public decimal ValorTotal { get; set; }
        public string? Protocolo { get; set; }
        public string? CStat { get; set; }
        public string? XMotivo { get; set; }
        public int Tentativas { get; set; }
    }

    public sealed class ComandaSemNota
    {
        public long Id { get; set; }
        public int Numero { get; set; }
        public string? Identificacao { get; set; }
        public DateTime FechadaEm { get; set; }
        public decimal ValorTotal { get; set; }
        public string? UltimaSituacao { get; set; }
        public string? UltimoMotivo { get; set; }
    }

    public sealed record CancelarNotaInput(string Justificativa);
    public sealed record InutilizarInput(int? Serie, int Inicial, int Final, string Justificativa);

    public static void MapFiscal(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/fiscal").WithTags("Fiscal (NFC-e)").RequireAuthorization(Permissoes.FiscalVer);
        var gEmitir = g.MapGroup("").RequireAuthorization(Permissoes.FiscalEmitir);
        var gCancelar = g.MapGroup("").RequireAuthorization(Permissoes.FiscalCancelar);

        g.MapGet("/status", (IOptionsMonitor<NfceOptions> o, NfceService nfce) =>
        {
            var opt = o.CurrentValue;
            object? cert = null;
            string? erroCert = null;
            if (opt.Configurada)
            {
                try
                {
                    var c = nfce.Certificado();
                    cert = new { titular = c.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false),
                                 validade = c.NotAfter, diasRestantes = (int)(c.NotAfter - DateTime.Now).TotalDays };
                }
                catch (Exception e) { erroCert = "Não foi possível abrir o certificado: " + e.Message; }
            }
            return Results.Ok(new
            {
                configurada = opt.Configurada,
                ambiente = opt.Ambiente,
                ambienteNome = opt.Ambiente == 1 ? "Produção" : "Homologação (sem valor fiscal)",
                serie = opt.Serie,
                certificado = cert,
                erroCertificado = erroCert,
                faltando = opt.Configurada ? null : new[]
                {
                    string.IsNullOrWhiteSpace(opt.CertificadoArquivo) || !File.Exists(opt.CertificadoArquivo) ? "Certificado A1 (.pfx) — Nfce__CertificadoArquivo / Nfce__CertificadoSenha" : null,
                    string.IsNullOrWhiteSpace(opt.CscId) || string.IsNullOrWhiteSpace(opt.Csc) ? "CSC (ID e código) — Nfce__CscId / Nfce__Csc" : null,
                }.Where(x => x is not null)
            });
        });

        g.MapGet("/status-sefaz", async (NfceService nfce, IOptionsMonitor<NfceOptions> o, CancellationToken ct) =>
        {
            if (!o.CurrentValue.Configurada) return Results.UnprocessableEntity(new { erro = "NFC-e não configurada." });
            try { var (cStat, xMotivo) = await nfce.StatusServicoAsync(ct); return Results.Ok(new { cStat, xMotivo, online = cStat == "107" }); }
            catch (SefazClient.FalhaComunicacao e) { return Results.Ok(new { cStat = "", xMotivo = e.Message, online = false }); }
        });

        g.MapGet("/notas", async (Db db, string? situacao, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<NotaResumo>("""
                select n.id, n.comanda_id, co.numero comanda_numero, n.serie, n.numero, n.chave, n.ambiente, n.tp_emis, n.situacao,
                       n.emitida_em, n.valor_total, n.protocolo, n.c_stat, n.x_motivo, n.tentativas
                  from nfce n left join comanda co on co.id = n.comanda_id
                 where (:situacao is null and n.emitida_em > sysdate - 2) or n.situacao = :situacao
                 order by n.id desc
                """, new { situacao = string.IsNullOrWhiteSpace(situacao) ? null : situacao });
        });

        // Comandas fechadas (7 dias) que ainda não têm NFC-e válida
        g.MapGet("/pendentes", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<ComandaSemNota>("""
                select co.id, co.numero, nvl(cl.nome, co.identificacao) identificacao, co.fechada_em, co.valor_total,
                       (select max(n.situacao) keep (dense_rank last order by n.id) from nfce n where n.comanda_id = co.id) ultima_situacao,
                       (select max(n.x_motivo) keep (dense_rank last order by n.id) from nfce n where n.comanda_id = co.id) ultimo_motivo
                  from comanda co left join cliente cl on cl.id = co.cliente_id
                 where co.situacao = 'F' and co.consumo_interno = 'N' and co.valor_total > 0
                   and co.fechada_em > sysdate - 7
                   and not exists (select 1 from nfce n where n.comanda_id = co.id
                                     and n.situacao in ('AUTORIZADA','CONTINGENCIA','PENDENTE','VERIFICAR','CANCELADA'))
                 order by co.fechada_em desc
                """);
        });

        gEmitir.MapPost("/comandas/{id:long}/emitir", async (NfceService nfce, Db db, long id, CancellationToken ct) =>
        {
            return Results.Ok(await nfce.EmitirAsync(id, ct));
        });

        gEmitir.MapPost("/notas/{id:long}/reenviar", async (NfceService nfce, Db db, long id, CancellationToken ct) =>
        {
            return Results.Ok(await nfce.ReenviarAsync(id, ct));
        });

        gCancelar.MapPost("/notas/{id:long}/cancelar", async (ClaimsPrincipal user, NfceService nfce, Db db, long id, CancelarNotaInput i, CancellationToken ct) =>
        {
            var just = i.Justificativa?.Trim() ?? "";
            if (just.Length is < 15 or > 255) return Results.UnprocessableEntity(new { erro = "Justificativa deve ter de 15 a 255 caracteres." });
            return Results.Ok(await nfce.CancelarAsync(id, just, user.Id(), ct));
        });

        gCancelar.MapPost("/inutilizar", async (ClaimsPrincipal user, NfceService nfce, Db db, IOptionsMonitor<NfceOptions> o, InutilizarInput i, CancellationToken ct) =>
        {
            var just = i.Justificativa?.Trim() ?? "";
            if (just.Length is < 15 or > 255) return Results.UnprocessableEntity(new { erro = "Justificativa deve ter de 15 a 255 caracteres." });
            if (i.Inicial <= 0 || i.Final < i.Inicial) return Results.UnprocessableEntity(new { erro = "Faixa de números inválida." });
            using (var c = await db.OpenAsync(ct))
            {
                var serie = i.Serie ?? o.CurrentValue.Serie;
                var usados = await c.ExecuteScalarAsync<int>("""
                    select count(*) from nfce where serie = :serie and ambiente = :amb and numero between :ini and :fim
                       and situacao in ('AUTORIZADA','CONTINGENCIA','CANCELADA','SUBSTITUIDA','PENDENTE')
                    """, new { serie, amb = o.CurrentValue.Ambiente.ToString(), ini = i.Inicial, fim = i.Final });
                if (usados > 0) return Results.UnprocessableEntity(new { erro = "A faixa contém números já usados em notas válidas." });
                if (o.CurrentValue.Ambiente == 1)
                {
                    // notas autorizadas pelo sistema antigo (Forms) na mesma série
                    var legado = await c.ExecuteScalarAsync<int>("""
                        select count(*) from administrativo.tb_bar_pedido_nfce
                         where modelo = '65' and to_number(serie) = :serie and numero_nff between :ini and :fim and protocolo is not null
                        """, new { serie, ini = i.Inicial, fim = i.Final });
                    if (legado > 0) return Results.UnprocessableEntity(new { erro = $"A faixa contém {legado} nota(s) autorizada(s) pelo sistema antigo." });
                }
            }
            return Results.Ok(await nfce.InutilizarAsync(i.Serie ?? o.CurrentValue.Serie, i.Inicial, i.Final, just, user.Id(), ct));
        });

        g.MapGet("/notas/{id:long}/danfe", async (NfceService nfce, Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var n = await nfce.ObterAsync(c, id);
            if (n?.Xml is null) return Results.NotFound();
            return Results.Content(Danfe.Html(Danfe.Ler(n.Xml, n.Protocolo, n.AutorizadaEm)), "text/html; charset=utf-8");
        });

        g.MapGet("/notas/{id:long}/xml", async (NfceService nfce, Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var n = await nfce.ObterAsync(c, id);
            if (n?.Xml is null) return Results.NotFound();
            var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + (n.XmlAutorizado ?? n.Xml);
            return Results.File(Encoding.UTF8.GetBytes(xml), "application/xml", $"{n.Chave}-nfce.xml");
        });
    }
}
