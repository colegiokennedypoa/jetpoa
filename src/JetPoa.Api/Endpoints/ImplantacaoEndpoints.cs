using System.Security.Claims;
using JetPoa.Api.Auth;
using System.Data;
using System.Globalization;
using System.Net.Sockets;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using JetPoa.Api.Fiscal;
using Microsoft.Extensions.Options;

namespace JetPoa.Api.Endpoints;

/// <summary>Ferramentas da virada: importar clientes e sincronizar produtos do Forms, e checklist de prontidão.</summary>
public static class ImplantacaoEndpoints
{
    private sealed class ContatoLegado
    {
        public string Fonte { get; set; } = "";
        public string? Nome { get; set; }
        public string? Doc { get; set; }
        public string? Tel { get; set; }
        public string? Email { get; set; }
        public string? Sexo { get; set; }
        public DateTime? Data { get; set; }
    }

    private sealed class ProdLegado
    {
        public long CdProduto { get; set; }
        public string DsProduto { get; set; } = "";
        public decimal VlProduto { get; set; }
        public string? FlAtivo { get; set; }
        public string? Ncm { get; set; }
        public string? Cfop { get; set; }
        public string? IcmsCsosn { get; set; }
        public string? PisCofins { get; set; }
        public long? NovoId { get; set; }
        public string? NovoNome { get; set; }
        public decimal? NovoPreco { get; set; }
        public string? NovoAtivo { get; set; }
        public string? NovoNcm { get; set; }
        public string? NovoCfop { get; set; }
        public string? NovoCsosn { get; set; }
        public string? NovoCst { get; set; }
    }

    public static void MapImplantacao(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/implantacao").WithTags("Implantação").RequireAuthorization(Permissoes.Implantacao);

        // ------------------------------------------------------------ clientes
        g.MapPost("/clientes/importar", async (Db db, bool? aplicar, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);

            var contatos = (await c.QueryAsync<ContatoLegado>("""
                select 'MAILING' fonte, nome, identificador doc, telefone tel, email, sexo, data from administrativo.tb_bar_mailing
                union all
                select 'RESERVA', nome, cpf, whatsapp, email, null, data_evento from administrativo.tb_bar_reserva
                union all
                select 'PEDIDO', nvl(nome, ds_identificacao), nu_documento, telefone, null, sexo, dt_pedido from administrativo.tb_bar_pedido
                union all
                select 'COMANDA', nvl(nome, ds_identificacao), ds_identificacao, telefone, null, sexo, dt_criacao from administrativo.tb_bar_mesa
                """)).ToList();

            // normaliza e agrupa por CPF (preferencial) ou celular; o registro mais recente prevalece
            var porChave = new Dictionary<string, (string Nome, string? Cpf, string? Cel, string? Email, string? Sexo, DateTime Data, string Fonte)>();
            var celularDoCpf = new Dictionary<string, string>(); // celular -> chave do CPF
            int semIdentificacao = 0, semNome = 0;
            foreach (var x in contatos.OrderBy(x => x.Data ?? DateTime.MinValue))
            {
                var cpf = Cpf(x.Doc);
                var cel = Celular(x.Tel) ?? (cpf is null ? Celular(x.Doc) : null);
                var nome = Nome(x.Nome);
                if (cpf is null && cel is null) { semIdentificacao++; continue; }
                var chave = cpf is not null ? "C" + cpf : "T" + cel;
                // contato só com celular que já apareceu com CPF: agrega ao CPF
                if (cpf is null && cel is not null && celularDoCpf.TryGetValue(cel, out var chaveCpf)) chave = chaveCpf;
                porChave.TryGetValue(chave, out var atual);
                nome ??= atual.Nome;
                if (nome is null) { semNome++; continue; }
                var reg = (nome, cpf ?? atual.Cpf, cel ?? atual.Cel, Email(x.Email) ?? atual.Email,
                           x.Sexo is "M" or "F" ? x.Sexo : atual.Sexo, x.Data ?? DateTime.MinValue, x.Fonte);
                porChave[chave] = reg;
                if (chave[0] == 'C' && reg.Item3 is not null)
                {
                    celularDoCpf[reg.Item3] = chave;
                    porChave.Remove("T" + reg.Item3); // o mesmo celular já visto sem CPF passa a pertencer ao CPF
                }
            }

            var existentesCpf = (await c.QueryAsync<string>("select cpf from cliente where cpf is not null")).ToHashSet();
            var existentesCel = (await c.QueryAsync<string>("select celular from cliente where celular is not null")).ToHashSet();
            var novos = porChave.Values.Where(v => !(v.Cpf is not null && existentesCpf.Contains(v.Cpf)) && !(v.Cel is not null && existentesCel.Contains(v.Cel))).ToList();

            // CPF repetido entre registros diferentes não pode (índice único)
            novos = novos.GroupBy(v => v.Cpf ?? Guid.NewGuid().ToString()).Select(gr => gr.OrderByDescending(v => v.Data).First()).ToList();

            if (aplicar == true)
            {
                using var tx = c.BeginTransaction();
                foreach (var v in novos)
                    await c.ExecuteAsync("""
                        insert into cliente (cpf, nome, celular, email, sexo, categoria_id, aceita_comunicacao, origem, observacao)
                        values (:Cpf, :Nome, :Cel, :Email, :Sexo, (select id from cliente_categoria where nome = 'Normal'), 'N', :origem, :obs)
                        """, new { v.Cpf, v.Nome, v.Cel, v.Email, v.Sexo, origem = "IMPORT_" + v.Fonte,
                                  obs = $"Importado do sistema antigo (último registro {v.Data:dd/MM/yyyy})" }, tx);
                tx.Commit();
            }

            return Results.Ok(new
            {
                aplicado = aplicar == true,
                registrosLidos = contatos.Count,
                porFonte = contatos.GroupBy(x => x.Fonte).ToDictionary(gr => gr.Key, gr => gr.Count()),
                semCpfNemCelular = semIdentificacao,
                semNome,
                clientesUnicos = porChave.Count,
                jaCadastrados = porChave.Count - novos.Count,
                novos = novos.Count,
                comCpf = novos.Count(v => v.Cpf is not null),
                comEmail = novos.Count(v => v.Email is not null),
                amostra = novos.OrderByDescending(v => v.Data).Take(8).Select(v => new { v.Nome, cpf = v.Cpf is null ? null : "***" + v.Cpf[^4..], v.Cel, v.Fonte })
            });
        });

        // ------------------------------------------------------------ produtos
        g.MapPost("/produtos/sincronizar", async (Db db, bool? aplicar, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);

            var lista = (await c.QueryAsync<ProdLegado>("""
                select l.cd_produto, trim(regexp_replace(l.ds_produto, ' {2,}', ' ')) ds_produto, l.vl_produto, l.fl_ativo,
                       case when regexp_like(trim(l.ncm), '^[0-9]{8}$') then trim(l.ncm) end ncm,
                       case when regexp_like(trim(l.cfop), '^[0-9]{4}$') then trim(l.cfop) end cfop,
                       substr(trim(l.icms_csosn), 1, 3) icms_csosn, substr(trim(l.pis_cofins), 1, 2) pis_cofins,
                       p.id novo_id, p.nome novo_nome, p.preco_venda novo_preco, p.ativo novo_ativo,
                       p.ncm novo_ncm, p.cfop novo_cfop, p.csosn novo_csosn, p.cst_pis_cofins novo_cst
                  from administrativo.tb_bar_produto l left join produto p on p.legado_cd_produto = l.cd_produto
                 where l.vl_produto >= 0
                """)).ToList();

            var novos = lista.Where(x => x.NovoId is null).ToList();
            var precos = lista.Where(x => x.NovoId is not null && x.NovoPreco != x.VlProduto).ToList();
            var ativos = lista.Where(x => x.NovoId is not null && (x.FlAtivo ?? "N") != x.NovoAtivo).ToList();
            var fiscais = lista.Where(x => x.NovoId is not null && x.NovoNcm is null && x.Ncm is not null).ToList();

            if (aplicar == true)
            {
                using var tx = c.BeginTransaction();
                foreach (var x in precos)
                    await c.ExecuteAsync("update produto set preco_venda = :VlProduto, alterado_em = sysdate where id = :NovoId", x, tx);
                foreach (var x in ativos)
                    await c.ExecuteAsync("update produto set ativo = :ativo, alterado_em = sysdate where id = :id", new { ativo = x.FlAtivo ?? "N", id = x.NovoId }, tx);
                foreach (var x in fiscais) // só preenche o que está vazio no sistema novo
                    await c.ExecuteAsync("""
                        update produto set ncm = nvl(ncm, :Ncm), cfop = nvl(cfop, :Cfop), csosn = nvl(csosn, :IcmsCsosn),
                               cst_pis_cofins = nvl(cst_pis_cofins, :PisCofins), alterado_em = sysdate where id = :NovoId
                        """, x, tx);
                if (novos.Count > 0)
                    await c.ExecuteAsync("""
                        insert into produto (nome, categoria_id, tipo, setor_producao_id, unidade, preco_venda, preco_custo, controla_estoque,
                                             estoque_atual, estoque_minimo, vendivel, ativo, ordem, ncm, cfop, csosn, cst_pis_cofins, legado_cd_produto)
                        select trim(regexp_replace(p.ds_produto, ' {2,}', ' ')), c.id, case when p.fl_tipo in ('P','K','S') then p.fl_tipo else 'P' end, s.id,
                               nvl(substr(upper(trim(p.tp_unidade)), 1, 6), 'UN'), p.vl_produto, p.vl_ultima_compra,
                               case when p.fl_tipo = 'P' then 'S' else 'N' end, nvl(p.qt_estoque, 0), p.qt_min_estoque,
                               nvl(p.fl_vendivel, 'S'), nvl(p.fl_ativo, 'N'), nvl(p.nu_ordem, 0),
                               case when regexp_like(trim(p.ncm), '^[0-9]{8}$') then trim(p.ncm) end,
                               case when regexp_like(trim(p.cfop), '^[0-9]{4}$') then trim(p.cfop) end,
                               substr(trim(p.icms_csosn), 1, 3), substr(trim(p.pis_cofins), 1, 2), p.cd_produto
                          from administrativo.tb_bar_produto p
                          join categoria c on c.legado_cd_tipo_produto = p.bartp_cd_tipo_produto
                          join setor_producao s on s.codigo = case p.tipo_comanda when 'C' then 'COZINHA' when 'B' then 'BAR' when 'G' then 'GARCOM' else 'NENHUM' end
                         where p.vl_produto >= 0 and not exists (select 1 from produto x where x.legado_cd_produto = p.cd_produto)
                        """, transaction: tx);
                tx.Commit();
            }

            return Results.Ok(new
            {
                aplicado = aplicar == true,
                novos = novos.Select(x => new { x.CdProduto, x.DsProduto, preco = x.VlProduto }),
                precosDiferentes = precos.Select(x => new { id = x.NovoId, nome = x.NovoNome, sistemaNovo = x.NovoPreco, forms = x.VlProduto }),
                situacaoDiferente = ativos.Select(x => new { id = x.NovoId, nome = x.NovoNome, sistemaNovo = x.NovoAtivo, forms = x.FlAtivo }),
                fiscalPreenchivel = fiscais.Count
            });
        });

        // ------------------------------------------------------------ checklist
        g.MapGet("/checklist", async (Db db, IOptionsMonitor<NfceOptions> o, NfceService nfce, bool? sefaz, CancellationToken ct) =>
        {
            var itens = new List<object>();
            void Item(string grupo, string titulo, string status, string detalhe) => itens.Add(new { grupo, titulo, status, detalhe });

            using var c = await db.OpenAsync(ct);
            var opt = o.CurrentValue;

            // usuários
            var perfis = (await c.QueryAsync<(string Perfil, decimal Qtd)>(
                "select p.nome, count(*) from usuario u join perfil p on p.id = u.perfil_id where u.ativo = 'S' group by p.nome")).ToDictionary(x => x.Perfil, x => (int)x.Qtd);
            Item("Usuários", "Administrador cadastrado", perfis.ContainsKey("Administrador") ? "ok" : "erro",
                perfis.Count == 0 ? "Nenhum usuário. Crie o Administrador na tela Usuários." : string.Join(", ", perfis.Select(p => $"{p.Key}: {p.Value}")));
            Item("Usuários", "Gerente para autorizar descontos", perfis.ContainsKey("Gerente") || perfis.ContainsKey("Administrador") ? "ok" : "aviso", "");
            Item("Usuários", "Operadores de caixa", perfis.ContainsKey("Caixa") ? "ok" : "aviso", perfis.ContainsKey("Caixa") ? "" : "Nenhum usuário com perfil Caixa.");

            // impressoras
            var setores = await c.QueryAsync<(string Nome, string Imprime, string? Host, decimal? Porta)>(
                "select nome, imprime, impressora_host, impressora_porta from setor_producao where ativo = 'S' and codigo <> 'NENHUM' order by id");
            // testa todas as impressoras ao mesmo tempo (2 s de limite cada)
            var testes = setores.Select(async s => (s, ok: s.Imprime == "S" && !string.IsNullOrWhiteSpace(s.Host)
                                                        && await TcpAbertaAsync(s.Host!, (int)(s.Porta ?? 9100)))).ToList();
            foreach (var (s, ok) in await Task.WhenAll(testes))
            {
                if (s.Imprime != "S" || string.IsNullOrWhiteSpace(s.Host)) { Item("Impressoras", s.Nome, "aviso", "Não configurada (tela Produção)."); continue; }
                Item("Impressoras", s.Nome, ok ? "ok" : "erro", $"{s.Host}:{s.Porta ?? 9100} " + (ok ? "respondendo" : "SEM RESPOSTA (rede/IP)"));
            }

            // NFC-e
            Item("NFC-e", "Configuração", opt.Configurada ? "ok" : "erro",
                opt.Configurada ? $"{(opt.Ambiente == 1 ? "PRODUÇÃO" : "HOMOLOGAÇÃO")} · série {opt.Serie}" : "Certificado/CSC não configurados (.conf).");
            Item("NFC-e", "Ambiente de produção", opt.Ambiente == 1 ? "ok" : "aviso",
                opt.Ambiente == 1 ? "Emitindo com valor fiscal." : "Em homologação: notas SEM valor fiscal. Na virada: Nfce__Ambiente=1 e CSC de produção.");
            if (opt.Configurada)
            {
                try
                {
                    var cert = nfce.Certificado();
                    var dias = (int)(cert.NotAfter - DateTime.Now).TotalDays;
                    Item("NFC-e", "Certificado A1", dias < 0 ? "erro" : dias < 30 ? "aviso" : "ok", $"Vence em {cert.NotAfter:dd/MM/yyyy} ({dias} dias).");
                }
                catch (Exception e) { Item("NFC-e", "Certificado A1", "erro", e.Message); }
                if (sefaz == true)
                {
                    try { var (cs, xm) = await nfce.StatusServicoAsync(ct); Item("NFC-e", "SEFAZ-RS", cs == "107" ? "ok" : "aviso", $"{cs} - {xm}"); }
                    catch (Exception e) { Item("NFC-e", "SEFAZ-RS", "erro", e.Message); }
                }
            }
            var pend = await c.ExecuteScalarAsync<int>("""
                select count(*) from produto where ativo = 'S' and vendivel = 'S' and tipo <> 'S' and taxa_tipo is null
                   and (ncm is null or cfop is null or csosn is null or cst_pis_cofins is null)
                """);
            Item("NFC-e", "Produtos com dados fiscais completos", pend == 0 ? "ok" : "aviso",
                pend == 0 ? "" : $"{pend} produto(s) ativos sem NCM/CFOP/CSOSN/CST: essas vendas ficam sem NFC-e (tela Produtos → pendência fiscal).");
            var legadoMax = await c.ExecuteScalarAsync<int>("""
                select nvl(max(numero_nff), 0) from administrativo.tb_bar_pedido_nfce where modelo = '65' and to_number(serie) = :serie
                """, new { serie = opt.Serie });
            var ultimoForms = await c.ExecuteScalarAsync<DateTime?>("select max(emissao) from administrativo.tb_bar_pedido_nfce");
            Item("NFC-e", "Numeração", "ok", opt.Ambiente == 1
                ? $"Próxima NFC-e de produção: série {opt.Serie}, nº {legadoMax + 1} ou seguinte (continua o Forms)."
                : $"Homologação usa numeração própria. Em produção, série {opt.Serie} continuará do nº {legadoMax + 1}.");
            Item("NFC-e", "Forms parado de emitir", "manual",
                $"Última NFC-e do Forms: {ultimoForms:dd/MM/yyyy HH:mm}. Na virada desative o job 401 (dbms_job.broken(401, true)) e pare de fechar comandas no Forms.");

            // cadastros
            var cadastros = await c.QuerySingleAsync<(decimal Clientes, decimal Produtos, decimal Garcons, decimal Areas)>("""
                select (select count(*) from cliente), (select count(*) from produto where ativo = 'S'),
                       (select count(*) from garcom where ativo = 'S'), (select count(*) from area where ativo = 'S') from dual
                """);
            Item("Cadastros", "Clientes", cadastros.Clientes > 0 ? "ok" : "aviso",
                cadastros.Clientes > 0 ? $"{cadastros.Clientes} cliente(s)." : "Nenhum cliente: use \"Importar clientes do Forms\".");
            Item("Cadastros", "Produtos / garçons / áreas", "ok", $"{cadastros.Produtos} produtos ativos · {cadastros.Garcons} garçons · {cadastros.Areas} áreas.");
            var difPreco = await c.ExecuteScalarAsync<int>("""
                select count(*) from administrativo.tb_bar_produto l join produto p on p.legado_cd_produto = l.cd_produto where l.vl_produto <> p.preco_venda
                """);
            var semImport = await c.ExecuteScalarAsync<int>("""
                select count(*) from administrativo.tb_bar_produto l where l.vl_produto >= 0 and not exists (select 1 from produto p where p.legado_cd_produto = l.cd_produto)
                """);
            Item("Cadastros", "Produtos sincronizados com o Forms", difPreco + semImport == 0 ? "ok" : "aviso",
                difPreco + semImport == 0 ? "" : $"{semImport} produto(s) novos no Forms e {difPreco} preço(s) diferente(s): use \"Sincronizar produtos\".");
            var evento = await c.ExecuteScalarAsync<string?>("select nome from evento where data = trunc(sysdate) and ativo = 'S'");
            Item("Operação", "Evento de hoje", "info", evento is null ? "Sem evento: nenhuma taxa automática no check-in." : evento);

            // Forms ainda em uso?
            var formsAberto = await c.ExecuteScalarAsync<int>("select count(*) from administrativo.tb_bar_mesa_produto");
            var ultimoPedidoForms = await c.ExecuteScalarAsync<DateTime?>("select max(dt_pedido) from administrativo.tb_bar_pedido");
            Item("Operação", "Comandas abertas no Forms", formsAberto == 0 ? "ok" : "aviso",
                $"{formsAberto} item(ns) lançado(s) em comandas abertas no Forms · último fechamento no Forms: {ultimoPedidoForms:dd/MM/yyyy HH:mm}.");

            return Results.Ok(itens);
        });
    }

    private static async Task<bool> TcpAbertaAsync(string host, int porta)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(host, porta, cts.Token);
            return true;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ normalização de contatos

    private static string? Cpf(string? v)
    {
        var d = Documento.SoDigitos(v);
        return d is { Length: 11 } && Documento.CpfValido(d) ? d : null;
    }

    /// <summary>Celular brasileiro com DDD (10–11 dígitos). Remove 0/55 iniciais; 9 dígitos sem DDD recebe 51 (Porto Alegre).</summary>
    private static string? Celular(string? v)
    {
        var d = Documento.SoDigitos(v);
        if (d is null) return null;
        if (d.Length is 12 or 13 && d.StartsWith("55")) d = d[2..];
        if (d.Length is 11 or 12 && d.StartsWith('0')) d = d[1..];
        if (d.Length == 9 && d[0] == '9') d = "51" + d;
        if (d.Length is not (10 or 11)) return null;
        if (d.Distinct().Count() <= 2) return null; // 99999999999, 51000000000...
        return d;
    }

    private static string? Email(string? v)
    {
        var e = v?.Trim().ToLowerInvariant();
        return e is not null && e.Contains('@') && e.Contains('.') && e.Length <= 200 ? e : null;
    }

    private static string? Nome(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var n = string.Join(' ', v.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (n.Length < 2 || n.Any(char.IsDigit) || n.ToUpperInvariant().Contains("TESTE")) return null;
        n = CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(n.ToLowerInvariant());
        return n.Length > 150 ? n[..150] : n;
    }
}
