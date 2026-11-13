using System.Security.Claims;
using JetPoa.Api.Auth;
using System.Data;
using System.Text.Json;
using Dapper;
using JetPoa.Api.Data;
using JetPoa.Api.Domain;
using static JetPoa.Api.Domain.Fechamento;

namespace JetPoa.Api.Endpoints;

public static class FechamentoEndpoints
{
    public sealed class FormaPagamento
    {
        public long Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string TpagNfce { get; set; } = "";
        public string AceitaTroco { get; set; } = "N";
    }

    public sealed class MotivoDesconto
    {
        public long Id { get; set; }
        public string Nome { get; set; } = "";
        public string ExigeObservacao { get; set; } = "N";
    }

    public sealed record PagamentoInput(long FormaPagamentoId, decimal Valor, decimal? ValorRecebido, string? Autorizacao);

    public sealed record PagamentoPrevioInput(long FormaPagamentoId, decimal Valor, string? Autorizacao, string? Observacao);

    public sealed class PagamentoPrevio
    {
        public long Id { get; set; }
        public long FormaPagamentoId { get; set; }
        public string Forma { get; set; } = "";
        public decimal Valor { get; set; }
        public string? Autorizacao { get; set; }
        public string? Observacao { get; set; }
        public DateTime CriadoEm { get; set; }
        public string? RegistradoPor { get; set; }
    }

    public sealed record SimularInput(Pedido Pedido);

    /// <summary>O operador é o usuário logado; Autorizador/PIN só quando outra pessoa (gerente) libera algo acima do limite.</summary>
    public sealed record FecharInput(
        Pedido Pedido, long? AutorizadorId, string? AutorizadorPin, List<PagamentoInput>? Pagamentos, string? CpfCnpjNota);

    public sealed record CancelarComandaInput(string Motivo, long? AutorizadorId, string? AutorizadorPin);

    private sealed class ItemRow
    {
        public long Id { get; set; }
        public long ProdutoId { get; set; }
        public string Produto { get; set; } = "";
        public long CategoriaId { get; set; }
        public string? TaxaTipo { get; set; }
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal? PrecoCusto { get; set; }
    }

    private sealed class CategoriaRow
    {
        public string Nome { get; set; } = "";
        public decimal DescontoPctPadrao { get; set; }
        public string IsentaEstacionamento { get; set; } = "N";
        public string IsentaEmbarque { get; set; } = "N";
        public string IsentaCouvert { get; set; } = "N";
        public string IsentaTaxaServico { get; set; } = "N";
    }

    private sealed record Contexto(string Situacao, List<Item> Itens, CategoriaCliente? Categoria, decimal TaxaPadrao);

    public static void MapFechamento(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Fechamento");
        var gFechar = g.MapGroup("").RequireAuthorization(Permissoes.ComandaFechar);

        g.MapGet("/fechamento/apoio", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var formas = await c.QueryAsync<FormaPagamento>(
                "select id, codigo, nome, tpag_nfce, aceita_troco from forma_pagamento where ativo = 'S' order by ordem");
            var motivos = await c.QueryAsync<MotivoDesconto>(
                "select id, nome, exige_observacao from motivo_desconto where ativo = 'S' order by id");
            // quem pode autorizar acima do limite do operador logado (gerentes/admins com PIN)
            var autorizadores = await c.QueryAsync<UsuarioEndpoints.Usuario>("""
                select u.id, u.nome, u.login, u.perfil_id, p.nome perfil, p.limite_desconto_pct, p.autoriza_desconto, u.ativo
                  from usuario u join perfil p on p.id = u.perfil_id
                 where u.ativo = 'S' and p.autoriza_desconto = 'S' and u.pin_hash is not null order by u.nome
                """);
            return new { formas, motivos, autorizadores, taxaServicoPadraoPct = await TaxaPadraoAsync(c, null) };
        });

        // Calcula sem gravar: mostra descontos, total e se precisa de autorização
        gFechar.MapPost("/comandas/{id:long}/fechamento/simular", async (ClaimsPrincipal user, Db db, long id, SimularInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var ctx = await CarregarAsync(c, id, null);
            if (ctx is null) return Results.NotFound();
            var r = Calcular(ctx.Itens, ctx.Categoria, i.Pedido ?? new Pedido(false, null, null, null), ctx.TaxaPadrao);
            var op = (await UsuarioEndpoints.ObterAsync(c, user.Id()))!;
            var exige = r.ConsumoInterno ? op.AutorizaDesconto != "S" : r.DescontoConcedidoPct > op.LimiteDescontoPct;
            return Results.Ok(new { calculo = r, limiteOperadorPct = op.LimiteDescontoPct, exigeAutorizacao = exige });
        });

        // Conferência (pré-conta): o cliente confere os itens antes de pagar. Página impressa pelo navegador
        // do caixa, na impressora padrão da máquina.
        gFechar.MapGet("/comandas/{id:long}/conferencia", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var (dados, erro) = await ConferenciaAsync(c, id);
            return dados is null
                ? Results.Content($"<p style='font-family:sans-serif'>{System.Net.WebUtility.HtmlEncode(erro)}</p>", "text/html; charset=utf-8", statusCode: 422)
                : Results.Content(Impressao.Conferencia.Html(dados), "text/html; charset=utf-8");
        });

        gFechar.MapPost("/comandas/{id:long}/fechar",async (ClaimsPrincipal user, Db db, Fiscal.NfceService nfce, Microsoft.Extensions.Options.IOptionsMonitor<Fiscal.NfceOptions> nfceOpt,
                                                       long id, FecharInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();

            var ctx = await CarregarAsync(c, id, tx, travar: true);
            if (ctx is null) return Results.NotFound(new { erro = "Comanda não encontrada." });
            if (ctx.Situacao != "A") return Erro("Comanda não está aberta.");
            if (ctx.Itens.Count == 0) return Erro("Comanda sem itens. Use cancelar comanda.");
            // regra do JetPOA: comanda só fecha com cliente cadastrado
            // (a constraint do banco só é reavaliada quando cliente_id muda, por isso a checagem aqui)
            if (await c.ExecuteScalarAsync<int>("select count(*) from comanda where id = :id and cliente_id is null", new { id }, tx) > 0)
                return Erro("Vincule um cliente cadastrado à comanda antes de fechar.");

            // operador = usuário logado
            var operador = await UsuarioEndpoints.ObterAsync(c, user.Id(), tx);
            if (operador is null) return Results.Unauthorized();

            var pedido = i.Pedido ?? new Pedido(false, null, null, null);
            var r = Calcular(ctx.Itens, ctx.Categoria, pedido, ctx.TaxaPadrao);
            if (r.Erros.Count > 0) return Erro(string.Join(" ", r.Erros));

            // motivos
            var motivos = (await c.QueryAsync<MotivoDesconto>(
                "select id, nome, exige_observacao from motivo_desconto where ativo = 'S'", transaction: tx)).ToDictionary(m => m.Id);
            foreach (var d in r.Descontos.Where(d => d.Origem == "MANUAL"))
            {
                if (!motivos.TryGetValue(d.MotivoId ?? 0, out var m)) return Erro("Motivo de desconto inválido.");
                if (m.ExigeObservacao == "S" && string.IsNullOrWhiteSpace(d.Observacao))
                    return Erro($"O motivo \"{m.Nome}\" exige observação.");
            }

            // alçada
            long? autorizadorId = null;
            var precisa = r.ConsumoInterno ? operador.AutorizaDesconto != "S" : r.DescontoConcedidoPct > operador.LimiteDescontoPct;
            if (precisa)
            {
                if (i.AutorizadorId is null) return Erro(r.ConsumoInterno
                    ? "Consumo interno exige autorização de gerente."
                    : $"Desconto de {r.DescontoConcedidoPct:0.##}% acima do limite do operador ({operador.LimiteDescontoPct:0.##}%). Peça autorização.");
                var aut = await UsuarioEndpoints.AutenticarAsync(c, i.AutorizadorId.Value, i.AutorizadorPin, tx);
                if (aut is null) return Erro("PIN do autorizador incorreto.");
                if (aut.AutorizaDesconto != "S") return Erro($"{aut.Nome} não pode autorizar descontos.");
                if (!r.ConsumoInterno && r.DescontoConcedidoPct > aut.LimiteDescontoPct)
                    return Erro($"Desconto de {r.DescontoConcedidoPct:0.##}% acima do limite de {aut.Nome} ({aut.LimiteDescontoPct:0.##}%).");
                autorizadorId = aut.Id;
            }
            else if (r.DescontoConcedido > 0 || r.ConsumoInterno)
            {
                autorizadorId = operador.Id;
            }

            // documento na nota
            var doc = Documento.SoDigitos(i.CpfCnpjNota);
            if (doc is not null && !(doc.Length == 11 ? Documento.CpfValido(doc) : doc.Length == 14 && Documento.CnpjValido(doc)))
                return Erro("CPF/CNPJ para a nota inválido.");

            // pagamentos
            var pagamentos = i.Pagamentos?.Where(p => p.Valor > 0).ToList() ?? [];
            var formas = (await c.QueryAsync<FormaPagamento>(
                "select id, codigo, nome, tpag_nfce, aceita_troco from forma_pagamento where ativo = 'S'", transaction: tx)).ToDictionary(f => f.Id);
            decimal troco = 0;
            foreach (var p in pagamentos)
            {
                if (!formas.TryGetValue(p.FormaPagamentoId, out var f)) return Erro("Forma de pagamento inválida.");
                if (p.ValorRecebido is not null)
                {
                    if (f.AceitaTroco != "S") return Erro($"{f.Nome} não aceita troco.");
                    if (p.ValorRecebido < p.Valor) return Erro("Valor recebido menor que o valor do pagamento.");
                    troco += p.ValorRecebido.Value - p.Valor;
                }
            }
            // pagamento prévio (já registrado na comanda) é abatido: cobra-se só a diferença
            var previo = await c.ExecuteScalarAsync<decimal>(
                "select nvl(sum(valor), 0) from comanda_pagamento where comanda_id = :id and previo = 'S'", new { id }, tx);
            if (previo > r.Total)
                return Erro($"Pagamento prévio ({previo:C}) maior que o total da conta ({r.Total:C}). Ajuste o pagamento prévio antes de fechar.");
            var restante = r.Total - previo;
            var pago = pagamentos.Sum(p => p.Valor);
            if (pago != restante)
                return Erro(pago < restante
                    ? $"Faltam {restante - pago:C} para fechar (total {r.Total:C}{(previo > 0 ? $", pago antes {previo:C}" : "")})."
                    : $"Pagamentos ({pago:C}) maiores que o valor a pagar ({restante:C}). Use \"valor recebido\" no dinheiro para troco.");

            // grava
            foreach (var (itemId, vdesc) in r.DescontoPorItem)
                await c.ExecuteAsync("update comanda_item set valor_desconto = :vdesc where id = :itemId", new { vdesc, itemId }, tx);

            foreach (var d in r.Descontos)
                await c.ExecuteAsync("""
                    insert into comanda_desconto (comanda_id, origem, escopo, comanda_item_id, categoria_id, taxa_tipo,
                                                  tipo_valor, informado, valor_calculado, motivo_id, observacao)
                    values (:id, :Origem, :Escopo, :ItemId, :CategoriaId, :TaxaTipo, :Tipo, :Informado, :Valor, :MotivoId, :Observacao)
                    """, new { id, d.Origem, d.Escopo, d.ItemId, d.CategoriaId, d.TaxaTipo, d.Tipo, d.Informado, d.Valor, d.MotivoId, d.Observacao }, tx);

            foreach (var p in pagamentos)
                await c.ExecuteAsync("""
                    insert into comanda_pagamento (comanda_id, forma_pagamento_id, valor, valor_recebido, autorizacao)
                    values (:id, :forma, :valor, :recebido, :aut)
                    """, new { id, forma = p.FormaPagamentoId, valor = p.Valor, recebido = p.ValorRecebido,
                              aut = string.IsNullOrWhiteSpace(p.Autorizacao) ? null : p.Autorizacao.Trim() }, tx);

            await c.ExecuteAsync("""
                update comanda
                   set situacao = 'F', fechada_em = sysdate, fechada_por = :op, consumo_interno = :interno,
                       subtotal = :subtotal, valor_desconto = :desconto, taxa_servico_pct = :pct,
                       valor_taxa_servico = :taxa, valor_total = :total, valor_troco = :troco,
                       cpf_cnpj_nota = :doc, desconto_autorizado_por = :aut
                 where id = :id
                """, new { id, op = operador.Id, interno = r.ConsumoInterno ? "S" : "N", subtotal = r.Subtotal,
                          desconto = r.DescontoTotal, pct = r.TaxaServicoPct, taxa = r.TaxaServico, total = r.Total,
                          troco, doc, aut = autorizadorId }, tx);

            // baixa de estoque da venda (kits baixam os componentes)
            await Estoque.BaixarVendaAsync(c, id, operador.Id, tx);

            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:op, 'COMANDA', :id, 'FECHAMENTO', :detalhe)
                """, new { op = operador.Id, id = id.ToString(), detalhe = new Clob(JsonSerializer.Serialize(new
                {
                    r.Subtotal, r.DescontoTotal, r.DescontoConcedidoPct, r.TaxaServico, r.Total, troco, previo,
                    autorizador = autorizadorId, descontos = r.Descontos.Select(d => new { d.Origem, d.Descricao, d.Valor, d.MotivoId, d.Observacao }),
                    pagamentos = pagamentos.Select(p => new { p.FormaPagamentoId, p.Valor })
                })) }, tx);

            tx.Commit();

            // NFC-e: a comanda já está fechada; falhas fiscais não desfazem o fechamento (ficam pendentes na tela Fiscal)
            Fiscal.NfceResultado? nota = null;
            if (!r.ConsumoInterno && r.Total > 0 && nfceOpt.CurrentValue.EmitirNoFechamento)
            {
                try
                {
                    // o DANFE é impresso pelo navegador do caixa, na impressora padrão da máquina
                    nota = await nfce.EmitirAsync(id, ct);
                }
                catch (Exception e)
                {
                    nota = new Fiscal.NfceResultado("ERRO", Mensagem: "Falha ao emitir NFC-e: " + e.Message);
                }
            }
            return Results.Ok(new { id, total = r.Total, troco, descontoTotal = r.DescontoTotal, nfce = nota });
        });

        // Pagamento prévio: registrado com a comanda aberta, abatido no fechamento
        gFechar.MapGet("/comandas/{id:long}/pagamentos-previos", async (Db db, long id, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            return await c.QueryAsync<PagamentoPrevio>("""
                select p.id, p.forma_pagamento_id, f.nome forma, p.valor, p.autorizacao, p.observacao, p.criado_em, u.nome registrado_por
                  from comanda_pagamento p
                  join forma_pagamento f on f.id = p.forma_pagamento_id
                  left join usuario u on u.id = p.registrado_por
                 where p.comanda_id = :id and p.previo = 'S'
                 order by p.id
                """, new { id });
        });

        gFechar.MapPost("/comandas/{id:long}/pagamentos-previos", async (ClaimsPrincipal user, Db db, long id, PagamentoPrevioInput i, CancellationToken ct) =>
        {
            if (i.Valor <= 0 || i.Valor > 999999) return Erro("Informe o valor pago.");
            var valor = Math.Round(i.Valor, 2);
            var obs = string.IsNullOrWhiteSpace(i.Observacao) ? null : i.Observacao.Trim();
            if (obs?.Length > 200) return Erro("Observação com no máximo 200 caracteres.");
            var aut = string.IsNullOrWhiteSpace(i.Autorizacao) ? null : i.Autorizacao.Trim();
            if (aut?.Length > 40) return Erro("NSU/autorização com no máximo 40 caracteres.");
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            var sit = await c.ExecuteScalarAsync<string?>("select situacao from comanda where id = :id for update", new { id }, tx);
            if (sit is null) return Results.NotFound(new { erro = "Comanda não encontrada." });
            if (sit != "A") return Erro("Comanda não está aberta.");
            if (await c.ExecuteScalarAsync<int>("select count(*) from forma_pagamento where id = :f and ativo = 'S'", new { f = i.FormaPagamentoId }, tx) == 0)
                return Erro("Forma de pagamento inválida.");
            await c.ExecuteAsync("""
                insert into comanda_pagamento (comanda_id, forma_pagamento_id, valor, autorizacao, previo, registrado_por, observacao)
                values (:id, :forma, :valor, :aut, 'S', :usr, :obs)
                """, new { id, forma = i.FormaPagamentoId, valor, aut, usr = user.Id(), obs }, tx);
            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:usr, 'COMANDA', :eid, 'PAGAMENTO_PREVIO', :det)
                """, new { usr = user.Id(), eid = id.ToString(), det = JsonSerializer.Serialize(new { i.FormaPagamentoId, valor, obs }) }, tx);
            tx.Commit();
            return Results.NoContent();
        });

        gFechar.MapDelete("/comandas/{id:long}/pagamentos-previos/{pid:long}", async (ClaimsPrincipal user, Db db, long id, long pid, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            var sit = await c.ExecuteScalarAsync<string?>("select situacao from comanda where id = :id for update", new { id }, tx);
            if (sit != "A") return Erro("Comanda não está aberta: o pagamento prévio já foi usado no fechamento.");
            var p = await c.QuerySingleOrDefaultAsync<(decimal FormaPagamentoId, decimal Valor)>(
                "select forma_pagamento_id, valor from comanda_pagamento where id = :pid and comanda_id = :id and previo = 'S'", new { id, pid }, tx);
            if (p == default) return Results.NotFound(new { erro = "Pagamento prévio não encontrado." });
            await c.ExecuteAsync("delete from comanda_pagamento where id = :pid", new { pid }, tx);
            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:usr, 'COMANDA', :eid, 'PAGAMENTO_PREVIO_REMOVIDO', :det)
                """, new { usr = user.Id(), eid = id.ToString(), det = JsonSerializer.Serialize(new { pagamentoId = pid, forma = (long)p.FormaPagamentoId, p.Valor }) }, tx);
            tx.Commit();
            return Results.NoContent();
        });

        // Cancela comanda inteira (ex.: aberta por engano). Com itens ativos exige quem autoriza desconto.
        gFechar.MapPost("/comandas/{id:long}/cancelar", async (ClaimsPrincipal user, Db db, long id, CancelarComandaInput i, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(i.Motivo) || i.Motivo.Trim().Length < 3) return Erro("Informe o motivo.");
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            var sit = await c.ExecuteScalarAsync<string?>("select situacao from comanda where id = :id for update", new { id }, tx);
            if (sit is null) return Results.NotFound();
            if (sit != "A") return Erro("Comanda não está aberta.");
            if (await c.ExecuteScalarAsync<int>("select count(*) from comanda_pagamento where comanda_id = :id", new { id }, tx) > 0)
                return Erro("Comanda tem pagamento prévio: remova (devolva ao cliente) antes de cancelar.");
            var ativos = await c.ExecuteScalarAsync<int>(
                "select count(*) from comanda_item where comanda_id = :id and situacao = 'A'", new { id }, tx);
            long? autorizador = null;
            if (ativos > 0 && !user.Pode(Permissoes.ComandaCancelar))
            {
                // caixa logado: precisa de um gerente autorizando com PIN
                if (i.AutorizadorId is null) return Erro("Comanda com itens: cancelamento exige autorização de gerente.");
                var aut = await UsuarioEndpoints.AutenticarAsync(c, i.AutorizadorId.Value, i.AutorizadorPin, tx);
                if (aut is null) return Erro("PIN do autorizador incorreto.");
                if (!Permissoes.DoPerfil(aut.Perfil).Contains(Permissoes.ComandaCancelar)) return Erro($"{aut.Nome} não pode autorizar cancelamento.");
                autorizador = aut.Id;
            }
            await c.ExecuteAsync("""
                update comanda set situacao = 'C', fechada_em = sysdate, fechada_por = :usr, motivo_cancelamento = :motivo, valor_total = 0,
                       desconto_autorizado_por = :aut
                 where id = :id
                """, new { id, usr = user.Id(), aut = autorizador, motivo = i.Motivo.Trim() }, tx);
            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:usr, 'COMANDA', :id, 'CANCELAMENTO', :detalhe)
                """, new { usr = user.Id(), id = id.ToString(), detalhe = JsonSerializer.Serialize(new { motivo = i.Motivo.Trim(), itensAtivos = ativos, autorizador }) }, tx);
            tx.Commit();
            return Results.NoContent();
        });
    }

    private static async Task<Contexto?> CarregarAsync(IDbConnection c, long id, IDbTransaction? tx, bool travar = false)
    {
        var sit = await c.ExecuteScalarAsync<string?>(
            "select situacao from comanda where id = :id" + (travar ? " for update" : ""), new { id }, tx);
        if (sit is null) return null;

        var itens = (await c.QueryAsync<ItemRow>("""
            select i.id, i.produto_id, p.nome produto, p.categoria_id, p.taxa_tipo, i.quantidade, i.preco_unitario, p.preco_custo
              from comanda_item i join produto p on p.id = i.produto_id
             where i.comanda_id = :id and i.situacao = 'A'
             order by i.id
            """, new { id }, tx))
            .Select(x => new Item(x.Id, x.ProdutoId, x.Produto, x.CategoriaId, x.TaxaTipo, x.Quantidade, x.PrecoUnitario, x.PrecoCusto))
            .ToList();

        var cat = await c.QuerySingleOrDefaultAsync<CategoriaRow>("""
            select cc.nome, cc.desconto_pct_padrao, cc.isenta_estacionamento, cc.isenta_embarque, cc.isenta_couvert, cc.isenta_taxa_servico
              from comanda co join cliente cl on cl.id = co.cliente_id join cliente_categoria cc on cc.id = cl.categoria_id
             where co.id = :id
            """, new { id }, tx);

        var categoria = cat is null ? null : new CategoriaCliente(cat.Nome, cat.DescontoPctPadrao,
            cat.IsentaEstacionamento == "S", cat.IsentaEmbarque == "S", cat.IsentaCouvert == "S", cat.IsentaTaxaServico == "S");
        return new Contexto(sit, itens, categoria, await TaxaPadraoAsync(c, tx));
    }

    private static async Task<(Impressao.Conferencia.Dados? Dados, string? Erro)> ConferenciaAsync(IDbConnection c, long id)
    {
        var ctx = await CarregarAsync(c, id, null);
        if (ctx is null) return (null, "Comanda não encontrada.");
        if (ctx.Situacao != "A") return (null, "Comanda não está aberta.");
        var cab = await c.QuerySingleAsync<(decimal Numero, string? Cliente, string? Mesa, DateTime AbertaEm)>("""
            select co.numero, nvl(cl.nome, co.identificacao), co.mesa, co.aberta_em
              from comanda co left join cliente cl on cl.id = co.cliente_id where co.id = :id
            """, new { id });
        var calculo = Calcular(ctx.Itens, ctx.Categoria, new Pedido(false, null, null, null), ctx.TaxaPadrao);
        var previo = await c.ExecuteScalarAsync<decimal>(
            "select nvl(sum(valor), 0) from comanda_pagamento where comanda_id = :id and previo = 'S'", new { id });
        return (new Impressao.Conferencia.Dados((int)cab.Numero, cab.Cliente, cab.Mesa, cab.AbertaEm, ctx.Itens, calculo, DateTime.Now, previo), null);
    }

    private static async Task<decimal> TaxaPadraoAsync(IDbConnection c, IDbTransaction? tx)
    {
        var v = await c.ExecuteScalarAsync<string?>("select valor from parametro where chave = 'TAXA_SERVICO_PCT'", transaction: tx);
        return decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static IResult Erro(string msg) => Results.UnprocessableEntity(new { erro = msg });
}
