using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Dapper;
using JetPoa.Api.Data;
using Microsoft.Extensions.Options;

namespace JetPoa.Api.Fiscal;

/// <param name="Nova">false quando a nota já existia (não reimprimir o DANFE automaticamente).</param>
public sealed record NfceResultado(string Situacao, long? Id = null, int? Numero = null, string? Chave = null, string? Mensagem = null, bool Nova = true);

/// <summary>Emissão, transmissão, contingência, cancelamento e inutilização da NFC-e.</summary>
public sealed class NfceService(Db db, IOptionsMonitor<NfceOptions> opcoes, ILogger<NfceService> log)
{
    private static readonly XNamespace Ns = NfceXml.Ns;
    private readonly SemaphoreSlim _trava = new(1, 1);
    private NfceOptions Opt => opcoes.CurrentValue;

    private X509Certificate2? _cert;
    private DateTime _certCarregadoEm;

    public X509Certificate2 Certificado()
    {
        if (_cert is null || DateTime.Now - _certCarregadoEm > TimeSpan.FromHours(1))
        {
            _cert = Assinador.CarregarCertificado(Opt.CertificadoArquivo!, Opt.CertificadoSenha);
            _certCarregadoEm = DateTime.Now;
        }
        return _cert;
    }

    private SefazClient Cliente() => new(Certificado(), Opt.TimeoutSegundos);

    // ------------------------------------------------------------------ modelos de leitura

    private sealed class EmitRow
    {
        public string Cnpj { get; set; } = ""; public string Ie { get; set; } = ""; public string RazaoSocial { get; set; } = "";
        public string? NomeFantasia { get; set; } public string Logradouro { get; set; } = ""; public string Numero { get; set; } = "";
        public string Bairro { get; set; } = ""; public string CodMunicipio { get; set; } = ""; public string Municipio { get; set; } = "";
        public string Uf { get; set; } = ""; public string Cep { get; set; } = ""; public string? Fone { get; set; }
        public string Crt { get; set; } = "1"; public decimal PcSimples { get; set; }
        public NfceXml.Emitente ToModel() => new(Cnpj, Ie, RazaoSocial, NomeFantasia, Logradouro, Numero, Bairro, CodMunicipio, Municipio, Uf, Cep, Fone, Crt, PcSimples);
    }

    private sealed class ItemRow
    {
        public long ProdutoId { get; set; } public string Nome { get; set; } = ""; public string? Ncm { get; set; } public string? Cest { get; set; }
        public string? Cfop { get; set; } public string? Csosn { get; set; } public string? CstPisCofins { get; set; } public string? Gtin { get; set; }
        public string Unidade { get; set; } = "UN"; public decimal Quantidade { get; set; } public decimal PrecoUnitario { get; set; }
        public decimal ValorDesconto { get; set; }
    }

    private sealed class PagRow { public string TpagNfce { get; set; } = "99"; public decimal Valor { get; set; } }

    private sealed class ComandaRow
    {
        public string Situacao { get; set; } = ""; public string ConsumoInterno { get; set; } = "N";
        public decimal? ValorTotal { get; set; } public string? CpfCnpjNota { get; set; }
    }

    public sealed class NfceRow
    {
        public long Id { get; set; } public long? ComandaId { get; set; } public string Cnpj { get; set; } = ""; public int Serie { get; set; }
        public int Numero { get; set; } public string Chave { get; set; } = ""; public string Ambiente { get; set; } = "2";
        public string TpEmis { get; set; } = "1"; public string Situacao { get; set; } = ""; public DateTime EmitidaEm { get; set; }
        public decimal ValorTotal { get; set; } public string? CpfCnpjDest { get; set; } public string? Xml { get; set; }
        public string? XmlAutorizado { get; set; } public string? Protocolo { get; set; } public DateTime? AutorizadaEm { get; set; }
        public string? CStat { get; set; } public string? XMotivo { get; set; } public int Tentativas { get; set; }
        public long? SubstituidaPor { get; set; } public string? CodigoNumerico { get; set; }
    }

    private const string SelectNfce = """
        select id, comanda_id, cnpj, serie, numero, chave, ambiente, tp_emis, situacao, emitida_em, valor_total,
               cpf_cnpj_dest, xml, xml_autorizado, protocolo, autorizada_em, c_stat, x_motivo, tentativas, substituida_por,
               substr(chave, 36, 8) codigo_numerico
          from nfce
        """;

    public async Task<NfceRow?> ObterAsync(IDbConnection c, long id) =>
        await c.QuerySingleOrDefaultAsync<NfceRow>(SelectNfce + " where id = :id", new { id });

    // ------------------------------------------------------------------ emissão

    public async Task<NfceResultado> EmitirAsync(long comandaId, CancellationToken ct)
    {
        if (!Opt.Configurada)
            return new("NAO_CONFIGURADA", Mensagem: "NFC-e não configurada (certificado A1 / CSC). A comanda ficou pendente de nota na tela Fiscal.");

        await _trava.WaitAsync(ct);
        try
        {
            using var c = await db.OpenAsync(ct);
            var existente = await c.QueryFirstOrDefaultAsync<NfceRow>(SelectNfce + """
                 where comanda_id = :comandaId and situacao in ('AUTORIZADA','CONTINGENCIA','PENDENTE','REJEITADA')
                 order by id desc
                """, new { comandaId });
            if (existente is not null)
                return existente.Situacao == "REJEITADA"
                    ? new("REJEITADA", existente.Id, existente.Numero, existente.Chave, $"Nota {existente.Numero} rejeitada: {existente.XMotivo}. Corrija e use Reenviar na tela Fiscal.", Nova: false)
                    : new(existente.Situacao, existente.Id, existente.Numero, existente.Chave, "NFC-e já emitida para esta comanda.", Nova: false);

            var com = await c.QuerySingleOrDefaultAsync<ComandaRow>(
                "select situacao, consumo_interno, valor_total, cpf_cnpj_nota from comanda where id = :comandaId", new { comandaId });
            if (com is null) return new("ERRO", Mensagem: "Comanda não encontrada.");
            if (com.Situacao != "F") return new("ERRO", Mensagem: "Comanda não está fechada.");
            if (com.ConsumoInterno == "S") return new("SEM_NOTA", Mensagem: "Consumo interno não gera NFC-e.");

            var (dados, erros) = await CarregarDadosAsync(c, comandaId, com, null, 0, null, DateTime.Now, 1, null);
            if (erros.Count > 0)
                return new("DADOS_FISCAIS", Mensagem: "Produtos sem dados fiscais válidos: " + string.Join(" | ", erros));
            if (dados is null) return new("SEM_NOTA", Mensagem: "Nenhum item tributável (somente serviços/taxas).");

            var id = await GerarEGravarAsync(c, comandaId, dados, tpEmis: 1);
            var r = await EnviarAsync(c, id, ct);

            if (r.Situacao == "VERIFICAR")
            {
                // SEFAZ não respondeu: emite em contingência offline com novo número
                log.LogWarning("NFC-e {Id} sem resposta da SEFAZ; emitindo em contingência.", id);
                var (dc, _) = await CarregarDadosAsync(c, comandaId, com, null, 0, null, DateTime.Now, 9, "Falha de comunicacao com o web service da SEFAZ");
                var idCont = await GerarEGravarAsync(c, comandaId, dc!, tpEmis: 9);
                await c.ExecuteAsync("update nfce set substituida_por = :idCont where id = :id", new { idCont, id });
                var nc = (await ObterAsync(c, idCont))!;
                return new("CONTINGENCIA", idCont, nc.Numero, nc.Chave, "SEFAZ sem resposta: NFC-e emitida em contingência offline; será transmitida automaticamente.");
            }
            return r;
        }
        finally { _trava.Release(); }
    }

    private async Task<(NfceXml.Dados? Dados, List<string> Erros)> CarregarDadosAsync(
        IDbConnection c, long comandaId, ComandaRow com, int? numeroFixo, int serieFixa, string? cNF, DateTime emissao, int tpEmis, string? xJust)
    {
        var itens = (await c.QueryAsync<ItemRow>("""
            select p.id produto_id, p.nome, p.ncm, p.cest, p.cfop, p.csosn, p.cst_pis_cofins, p.gtin, p.unidade,
                   i.quantidade, i.preco_unitario, i.valor_desconto
              from comanda_item i join produto p on p.id = i.produto_id
             where i.comanda_id = :comandaId and i.situacao = 'A'
               and p.tipo <> 'S' and p.taxa_tipo is null
               and i.quantidade * i.preco_unitario - i.valor_desconto > 0
             order by i.id
            """, new { comandaId })).ToList();
        if (itens.Count == 0) return (null, []);

        var erros = NfceXml.Validar(itens.Select(i => (i.Nome, i.Ncm, i.Cfop, i.Csosn, i.CstPisCofins)).Distinct());
        if (erros.Count > 0) return (null, erros);

        var emit = (await c.QueryFirstAsync<EmitRow>("select * from emitente order by id")).ToModel();
        var vNF = itens.Sum(i => Math.Round(i.Quantidade * i.PrecoUnitario, 2) - i.ValorDesconto);

        // pagamentos da comanda proporcionais ao valor da nota (serviços e taxa de serviço ficam fora da NFC-e)
        var pags = (await c.QueryAsync<PagRow>("""
            select f.tpag_nfce, p.valor from comanda_pagamento p join forma_pagamento f on f.id = p.forma_pagamento_id
             where p.comanda_id = :comandaId order by p.id
            """, new { comandaId })).ToList();
        var totalPago = pags.Sum(p => p.Valor);
        var pagNf = new List<NfceXml.Pagamento>();
        if (totalPago <= 0) pagNf.Add(new("99", vNF));
        else
        {
            foreach (var p in pags) pagNf.Add(new(p.TpagNfce, Math.Round(p.Valor * vNF / totalPago, 2)));
            var resto = vNF - pagNf.Sum(p => p.Valor);
            if (resto != 0)
            {
                var k = pagNf.IndexOf(pagNf.MaxBy(p => p.Valor)!);
                pagNf[k] = pagNf[k] with { Valor = pagNf[k].Valor + resto };
            }
            pagNf.RemoveAll(p => p.Valor <= 0);
        }

        var serie = serieFixa > 0 ? serieFixa : Opt.Serie;
        var numero = numeroFixo ?? 0; // definido na gravação
        var doc = string.IsNullOrEmpty(com.CpfCnpjNota) ? null : com.CpfCnpjNota;
        var dados = new NfceXml.Dados(emit, serie, numero, cNF ?? "", emissao, Opt.Ambiente, tpEmis,
            tpEmis == 9 ? emissao : null, xJust, doc,
            itens.Select(i => new NfceXml.Item(i.ProdutoId, i.Nome, i.Ncm!, i.Cest, i.Cfop!, i.Csosn!, i.CstPisCofins!, i.Gtin,
                i.Unidade, i.Quantidade, i.PrecoUnitario, i.ValorDesconto)).ToList(),
            pagNf);
        return (dados, []);
    }

    /// <summary>Reserva o número (transação), monta, assina e grava a nota como PENDENTE ou CONTINGENCIA.</summary>
    private async Task<long> GerarEGravarAsync(IDbConnection c, long? comandaId, NfceXml.Dados d, int tpEmis)
    {
        using var tx = c.BeginTransaction();
        var numero = d.Numero;
        if (numero == 0)
        {
            numero = await c.ExecuteScalarAsync<int>(
                "select ultimo_numero + 1 from nfce_numeracao where cnpj = :cnpj and serie = :serie for update",
                new { cnpj = d.Emit.Cnpj, serie = d.Serie }, tx);
            await c.ExecuteAsync("update nfce_numeracao set ultimo_numero = :numero where cnpj = :cnpj and serie = :serie",
                new { numero, cnpj = d.Emit.Cnpj, serie = d.Serie }, tx);
        }
        var cNF = string.IsNullOrEmpty(d.CodigoNumerico) ? NfceXml.CodigoNumericoAleatorio(numero) : d.CodigoNumerico;
        var dados = d with { Numero = numero, CodigoNumerico = cNF, TpEmis = tpEmis };
        var (xml, chave, vNF) = MontarAssinado(dados);

        var id = await c.ExecuteScalarAsync<long>("select sq_nfce.nextval from dual", transaction: tx);
        await c.ExecuteAsync("""
            insert into nfce (id, comanda_id, cnpj, serie, numero, chave, ambiente, tp_emis, situacao, emitida_em, valor_total, cpf_cnpj_dest, xml)
            values (:id, :comandaId, :cnpj, :serie, :numero, :chave, :amb, :tpEmis, :sit, :emissao, :vNF, :dest, :xml)
            """, new { id, comandaId, cnpj = d.Emit.Cnpj, serie = d.Serie, numero, chave, amb = Opt.Ambiente.ToString(),
                      tpEmis = tpEmis.ToString(), sit = tpEmis == 9 ? "CONTINGENCIA" : "PENDENTE", emissao = d.Emissao,
                      vNF, dest = d.CpfCnpjDest, xml = new Clob(xml) }, tx);
        tx.Commit();
        return id;
    }

    /// <summary>Monta + assina + QR Code. Público para testes (aceita certificado externo).</summary>
    public (string Xml, string Chave, decimal VNF) MontarAssinado(NfceXml.Dados d, X509Certificate2? cert = null, string? cscId = null, string? csc = null)
    {
        var (nfe, chave, vNF) = NfceXml.Montar(d);
        var (assinado, digest) = Assinador.Assinar(nfe, "infNFe", cert ?? Certificado());
        var qr = d.TpEmis == 9
            ? NfceXml.QrCodeOffline(Opt.UrlQrCode, chave, d.Ambiente, d.Emissao, vNF, digest, cscId ?? Opt.CscId!, csc ?? Opt.Csc!)
            : NfceXml.QrCodeOnline(Opt.UrlQrCode, chave, d.Ambiente, cscId ?? Opt.CscId!, csc ?? Opt.Csc!);
        NfceXml.AdicionarSupl(assinado, qr, Opt.UrlConsultaChave);
        return (assinado.ToString(SaveOptions.DisableFormatting), chave, vNF);
    }

    // ------------------------------------------------------------------ transmissão

    public async Task<NfceResultado> EnviarAsync(IDbConnection c, long id, CancellationToken ct)
    {
        var n = (await ObterAsync(c, id))!;
        var envi = new XElement(Ns + "enviNFe", new XAttribute("versao", "4.00"),
            new XElement(Ns + "idLote", n.Numero), new XElement(Ns + "indSinc", 1), XElement.Parse(n.Xml!));

        XElement ret;
        try
        {
            using var cli = Cliente();
            ret = await cli.AutorizarAsync(Opt.UrlsAtuais.Autorizacao, envi, ct);
        }
        catch (SefazClient.FalhaComunicacao e)
        {
            await c.ExecuteAsync("""
                update nfce set tentativas = tentativas + 1, ultimo_envio_em = sysdate, x_motivo = :msg,
                       situacao = case when tp_emis = '9' then 'CONTINGENCIA' else 'VERIFICAR' end
                 where id = :id
                """, new { id, msg = Trunca(e.Message, 500) });
            return new(n.TpEmis == "9" ? "CONTINGENCIA" : "VERIFICAR", id, n.Numero, n.Chave, e.Message);
        }

        var cStatLote = Val(ret, "cStat");
        var prot = ret.Descendants(Ns + "protNFe").FirstOrDefault();
        var cStat = prot is null ? cStatLote : Val(prot, "cStat");
        var xMotivo = prot is null ? Val(ret, "xMotivo") : Val(prot, "xMotivo");

        if (cStat == "204") // duplicidade: já autorizada antes (ex.: resposta perdida)
        {
            var cons = await ConsultarChaveAsync(n.Chave, ct);
            if (cons.CStat == "100") { prot = cons.Prot; cStat = "100"; xMotivo = cons.XMotivo; }
        }

        if (cStat is "100" or "150" && prot is not null)
        {
            await GravarAutorizacaoAsync(c, n, prot, ret);
            return new("AUTORIZADA", id, n.Numero, n.Chave, xMotivo);
        }

        await c.ExecuteAsync("""
            update nfce set situacao = 'REJEITADA', c_stat = :cStat, x_motivo = :xMotivo, tentativas = tentativas + 1,
                   ultimo_envio_em = sysdate, ultimo_retorno = :ret
             where id = :id
            """, new { id, cStat, xMotivo = Trunca(xMotivo, 500), ret = new Clob(ret.ToString(SaveOptions.DisableFormatting)) });
        return new("REJEITADA", id, n.Numero, n.Chave, $"{cStat} - {xMotivo}");
    }

    private async Task GravarAutorizacaoAsync(IDbConnection c, NfceRow n, XElement prot, XElement ret)
    {
        var nfeProc = new XElement(Ns + "nfeProc", new XAttribute("versao", "4.00"), XElement.Parse(n.Xml!), new XElement(prot));
        var inf = prot.Element(Ns + "infProt")!;
        await c.ExecuteAsync("""
            update nfce set situacao = 'AUTORIZADA', protocolo = :prot, autorizada_em = sysdate, c_stat = :cStat, x_motivo = :xMotivo,
                   xml_autorizado = :proc, tentativas = tentativas + 1, ultimo_envio_em = sysdate, ultimo_retorno = :ret
             where id = :id
            """, new { id = n.Id, prot = Val(inf, "nProt"), cStat = Val(inf, "cStat"), xMotivo = Trunca(Val(inf, "xMotivo"), 500),
                      proc = new Clob(nfeProc.ToString(SaveOptions.DisableFormatting)), ret = new Clob(ret.ToString(SaveOptions.DisableFormatting)) });
    }

    public async Task<(string CStat, string XMotivo, XElement? Prot)> ConsultarChaveAsync(string chave, CancellationToken ct)
    {
        var cons = new XElement(Ns + "consSitNFe", new XAttribute("versao", "4.00"),
            new XElement(Ns + "tpAmb", Opt.Ambiente), new XElement(Ns + "xServ", "CONSULTAR"), new XElement(Ns + "chNFe", chave));
        using var cli = Cliente();
        var ret = await cli.ConsultarAsync(Opt.UrlsAtuais.Consulta, cons, ct);
        var prot = ret.Element(Ns + "protNFe");
        return (Val(ret, "cStat"), Val(ret, "xMotivo"), prot);
    }

    public async Task<(string CStat, string XMotivo)> StatusServicoAsync(CancellationToken ct)
    {
        var cons = new XElement(Ns + "consStatServ", new XAttribute("versao", "4.00"),
            new XElement(Ns + "tpAmb", Opt.Ambiente), new XElement(Ns + "cUF", NfceXml.CUfRs), new XElement(Ns + "xServ", "STATUS"));
        using var cli = Cliente();
        var ret = await cli.StatusAsync(Opt.UrlsAtuais.Status, cons, ct);
        return (Val(ret, "cStat"), Val(ret, "xMotivo"));
    }

    // ------------------------------------------------------------------ reenvio de rejeitada

    public async Task<NfceResultado> ReenviarAsync(long id, CancellationToken ct)
    {
        await _trava.WaitAsync(ct);
        try
        {
            using var c = await db.OpenAsync(ct);
            var n = await ObterAsync(c, id);
            if (n is null) return new("ERRO", Mensagem: "Nota não encontrada.");
            if (n.Situacao != "REJEITADA" || n.ComandaId is null) return new("ERRO", Mensagem: "Só notas rejeitadas podem ser reenviadas.");
            var com = await c.QuerySingleAsync<ComandaRow>(
                "select situacao, consumo_interno, valor_total, cpf_cnpj_nota from comanda where id = :id", new { id = n.ComandaId });
            var (dados, erros) = await CarregarDadosAsync(c, n.ComandaId.Value, com, n.Numero, n.Serie, n.CodigoNumerico, DateTime.Now, int.Parse(n.TpEmis), null);
            if (erros.Count > 0) return new("DADOS_FISCAIS", id, n.Numero, Mensagem: "Ainda há produtos com dados fiscais inválidos: " + string.Join(" | ", erros));
            if (dados is null) return new("ERRO", Mensagem: "Sem itens tributáveis.");
            var (xml, chave, vNF) = MontarAssinado(dados);
            await c.ExecuteAsync("""
                update nfce set xml = :xml, chave = :chave, valor_total = :vNF, emitida_em = :emissao, situacao = 'PENDENTE', x_motivo = null
                 where id = :id
                """, new { id, xml = new Clob(xml), chave, vNF, emissao = dados.Emissao });
            return await EnviarAsync(c, id, ct);
        }
        finally { _trava.Release(); }
    }

    // ------------------------------------------------------------------ cancelamento

    public async Task<NfceResultado> CancelarAsync(long id, string justificativa, long usuarioId, CancellationToken ct)
    {
        using var c = await db.OpenAsync(ct);
        var n = await ObterAsync(c, id);
        if (n is null) return new("ERRO", Mensagem: "Nota não encontrada.");
        if (n.Situacao != "AUTORIZADA" || n.Protocolo is null) return new("ERRO", Mensagem: "Só notas autorizadas podem ser canceladas.");
        var (cStat, xMotivo, protEvento) = await EnviarCancelamentoAsync(n, justificativa, ct);
        if (cStat is "135" or "155")
        {
            await c.ExecuteAsync("""
                update nfce set situacao = 'CANCELADA', cancelamento_protocolo = :prot, cancelamento_justificativa = :just,
                       cancelada_em = sysdate, cancelada_por = :usuarioId, c_stat = :cStat, x_motivo = :xMotivo
                 where id = :id
                """, new { id, prot = protEvento, just = justificativa, usuarioId, cStat, xMotivo = Trunca(xMotivo, 500) });
            return new("CANCELADA", id, n.Numero, n.Chave, xMotivo);
        }
        return new("ERRO", id, n.Numero, n.Chave, $"{cStat} - {xMotivo}");
    }

    private async Task<(string CStat, string XMotivo, string? Protocolo)> EnviarCancelamentoAsync(NfceRow n, string justificativa, CancellationToken ct)
    {
        var idEvento = $"ID110111{n.Chave}01";
        var evento = new XElement(Ns + "evento", new XAttribute("versao", "1.00"),
            new XElement(Ns + "infEvento", new XAttribute("Id", idEvento),
                new XElement(Ns + "cOrgao", NfceXml.CUfRs), new XElement(Ns + "tpAmb", n.Ambiente), new XElement(Ns + "CNPJ", n.Cnpj),
                new XElement(Ns + "chNFe", n.Chave), new XElement(Ns + "dhEvento", NfceXml.DataHora(DateTime.Now)),
                new XElement(Ns + "tpEvento", "110111"), new XElement(Ns + "nSeqEvento", 1), new XElement(Ns + "verEvento", "1.00"),
                new XElement(Ns + "detEvento", new XAttribute("versao", "1.00"),
                    new XElement(Ns + "descEvento", "Cancelamento"), new XElement(Ns + "nProt", n.Protocolo),
                    new XElement(Ns + "xJust", NfceXml.Limpa(justificativa)))));
        var (assinado, _) = Assinador.Assinar(evento, "infEvento", Certificado());
        var env = new XElement(Ns + "envEvento", new XAttribute("versao", "1.00"), new XElement(Ns + "idLote", n.Numero), assinado);
        using var cli = Cliente();
        var ret = await cli.EventoAsync(Opt.UrlsAtuais.Evento, env, ct);
        var inf = ret.Descendants(Ns + "infEvento").FirstOrDefault();
        return inf is null ? (Val(ret, "cStat"), Val(ret, "xMotivo"), null) : (Val(inf, "cStat"), Val(inf, "xMotivo"), Val(inf, "nProt"));
    }

    // ------------------------------------------------------------------ inutilização

    public async Task<NfceResultado> InutilizarAsync(int serie, int inicial, int final, string justificativa, long? usuarioId, CancellationToken ct)
    {
        using var c = await db.OpenAsync(ct);
        var cnpj = await c.ExecuteScalarAsync<string>("select cnpj from emitente where rownum = 1");
        var ano = DateTime.Now.ToString("yy");
        var idInut = $"ID{NfceXml.CUfRs}{ano}{cnpj}65{serie:000}{inicial:000000000}{final:000000000}";
        var inut = new XElement(Ns + "inutNFe", new XAttribute("versao", "4.00"),
            new XElement(Ns + "infInut", new XAttribute("Id", idInut),
                new XElement(Ns + "tpAmb", Opt.Ambiente), new XElement(Ns + "xServ", "INUTILIZAR"), new XElement(Ns + "cUF", NfceXml.CUfRs),
                new XElement(Ns + "ano", ano), new XElement(Ns + "CNPJ", cnpj), new XElement(Ns + "mod", 65), new XElement(Ns + "serie", serie),
                new XElement(Ns + "nNFIni", inicial), new XElement(Ns + "nNFFin", final), new XElement(Ns + "xJust", NfceXml.Limpa(justificativa))));
        var (assinado, _) = Assinador.Assinar(inut, "infInut", Certificado());
        XElement ret;
        using (var cli = Cliente()) ret = await cli.InutilizarAsync(Opt.UrlsAtuais.Inutilizacao, assinado, ct);
        var inf = ret.Element(Ns + "infInut") ?? ret;
        var cStat = Val(inf, "cStat");
        var ok = cStat == "102";
        await c.ExecuteAsync("""
            insert into nfce_inutilizacao (cnpj, serie, numero_inicial, numero_final, ambiente, justificativa, situacao, protocolo, c_stat, x_motivo, xml_retorno, criado_por)
            values (:cnpj, :serie, :inicial, :final, :amb, :just, :sit, :prot, :cStat, :xMotivo, :xml, :usuarioId)
            """, new { cnpj, serie, inicial, final, amb = Opt.Ambiente.ToString(), just = justificativa, sit = ok ? "HOMOLOGADA" : "REJEITADA",
                      prot = Val(inf, "nProt"), cStat, xMotivo = Trunca(Val(inf, "xMotivo"), 500), xml = new Clob(ret.ToString(SaveOptions.DisableFormatting)), usuarioId });
        if (ok)
            await c.ExecuteAsync("""
                update nfce set situacao = 'INUTILIZADA'
                 where cnpj = :cnpj and serie = :serie and numero between :inicial and :final and situacao in ('REJEITADA','VERIFICAR')
                """, new { cnpj, serie, inicial, final });
        return new(ok ? "INUTILIZADA" : "ERRO", Mensagem: $"{cStat} - {Val(inf, "xMotivo")}");
    }

    // ------------------------------------------------------------------ rotina automática

    /// <summary>Transmite contingências e resolve notas sem resposta (chamado pelo NfceWorker).</summary>
    public async Task ProcessarPendenciasAsync(CancellationToken ct)
    {
        if (!Opt.Configurada) return;
        await _trava.WaitAsync(ct);
        try
        {
            using var c = await db.OpenAsync(ct);
            var pendentes = (await c.QueryAsync<NfceRow>(SelectNfce + """
                 where situacao = 'CONTINGENCIA'
                    or (situacao in ('VERIFICAR','PENDENTE') and nvl(ultimo_envio_em, emitida_em) < sysdate - 1/1440)
                 order by id
                """)).ToList();

            foreach (var n in pendentes)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    if (n.Situacao == "CONTINGENCIA") { await EnviarAsync(c, n.Id, ct); continue; }

                    // VERIFICAR / PENDENTE antiga: a SEFAZ recebeu?
                    var (cStat, xMotivo, prot) = await ConsultarChaveAsync(n.Chave, ct);
                    if (cStat == "100" && prot is not null)
                    {
                        await GravarAutorizacaoAsync(c, n, prot, prot);
                        if (n.SubstituidaPor is not null)
                        {
                            var aut = (await ObterAsync(c, n.Id))!;
                            var (cs, xm, protCanc) = await EnviarCancelamentoAsync(aut, "Nota substituida por emissao em contingencia offline", ct);
                            await c.ExecuteAsync("""
                                update nfce set situacao = case when :ok = 1 then 'SUBSTITUIDA' else situacao end,
                                       cancelamento_protocolo = :protCanc, c_stat = :cs, x_motivo = :xm, cancelada_em = sysdate
                                 where id = :id
                                """, new { id = n.Id, ok = cs is "135" or "155" ? 1 : 0, protCanc, cs, xm = Trunca(xm, 500) });
                        }
                    }
                    else if (cStat == "217") // não consta na SEFAZ
                    {
                        if (n.SubstituidaPor is not null)
                            await InutilizarAsync(n.Serie, n.Numero, n.Numero, "Numero nao utilizado - nota substituida por contingencia offline", null, ct);
                        else
                            await EnviarAsync(c, n.Id, ct);
                    }
                    else
                    {
                        await c.ExecuteAsync("update nfce set ultimo_envio_em = sysdate, x_motivo = :m where id = :id",
                            new { id = n.Id, m = Trunca($"Consulta: {cStat} - {xMotivo}", 500) });
                    }
                }
                catch (SefazClient.FalhaComunicacao) { break; } // SEFAZ ainda fora: tenta no próximo ciclo
                catch (Exception e) { log.LogError(e, "Falha ao processar NFC-e {Id}", n.Id); }
            }
        }
        finally { _trava.Release(); }
    }

    private static string Val(XElement e, string nome) => e.Descendants(Ns + nome).FirstOrDefault()?.Value ?? e.Element(Ns + nome)?.Value ?? "";
    private static string Trunca(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;
}
