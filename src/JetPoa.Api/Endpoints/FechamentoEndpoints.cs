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

    public sealed record SimularInput(Pedido Pedido, long? OperadorId);

    public sealed record FecharInput(
        Pedido Pedido, long OperadorId, string OperadorPin, long? AutorizadorId, string? AutorizadorPin,
        List<PagamentoInput>? Pagamentos, string? CpfCnpjNota);

    public sealed record CancelarComandaInput(string Motivo, long UsuarioId, string Pin);

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

        g.MapGet("/fechamento/apoio", async (Db db, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var formas = await c.QueryAsync<FormaPagamento>(
                "select id, codigo, nome, tpag_nfce, aceita_troco from forma_pagamento where ativo = 'S' order by ordem");
            var motivos = await c.QueryAsync<MotivoDesconto>(
                "select id, nome, exige_observacao from motivo_desconto where ativo = 'S' order by id");
            var operadores = await c.QueryAsync<UsuarioEndpoints.Usuario>("""
                select u.id, u.nome, u.login, u.perfil_id, p.nome perfil, p.limite_desconto_pct, p.autoriza_desconto, u.ativo
                  from usuario u join perfil p on p.id = u.perfil_id
                 where u.ativo = 'S' and p.nome <> 'Garçom' order by u.nome
                """);
            return new { formas, motivos, operadores, taxaServicoPadraoPct = await TaxaPadraoAsync(c, null) };
        });

        // Calcula sem gravar: mostra descontos, total e se precisa de autorização
        g.MapPost("/comandas/{id:long}/fechamento/simular", async (Db db, long id, SimularInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            var ctx = await CarregarAsync(c, id, null);
            if (ctx is null) return Results.NotFound();
            var r = Calcular(ctx.Itens, ctx.Categoria, i.Pedido ?? new Pedido(false, null, null, null), ctx.TaxaPadrao);
            decimal? limite = null;
            if (i.OperadorId is not null)
                limite = await c.ExecuteScalarAsync<decimal?>(
                    "select p.limite_desconto_pct from usuario u join perfil p on p.id = u.perfil_id where u.id = :op",
                    new { op = i.OperadorId });
            var exige = r.ConsumoInterno || (limite is not null && r.DescontoConcedidoPct > limite);
            return Results.Ok(new { calculo = r, limiteOperadorPct = limite, exigeAutorizacao = exige });
        });

        g.MapPost("/comandas/{id:long}/fechar", async (Db db, Fiscal.NfceService nfce, Microsoft.Extensions.Options.IOptionsMonitor<Fiscal.NfceOptions> nfceOpt,
                                                       long id, FecharInput i, CancellationToken ct) =>
        {
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();

            var ctx = await CarregarAsync(c, id, tx, travar: true);
            if (ctx is null) return Results.NotFound(new { erro = "Comanda não encontrada." });
            if (ctx.Situacao != "A") return Erro("Comanda não está aberta.");
            if (ctx.Itens.Count == 0) return Erro("Comanda sem itens. Use cancelar comanda.");

            // operador
            var operador = await UsuarioEndpoints.AutenticarAsync(c, i.OperadorId, i.OperadorPin, tx);
            if (operador is null) return Erro("PIN do operador incorreto.");

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
            var pago = pagamentos.Sum(p => p.Valor);
            if (pago != r.Total)
                return Erro(pago < r.Total
                    ? $"Faltam {r.Total - pago:C} para fechar (total {r.Total:C})."
                    : $"Pagamentos ({pago:C}) maiores que o total ({r.Total:C}). Use \"valor recebido\" no dinheiro para troco.");

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
                    r.Subtotal, r.DescontoTotal, r.DescontoConcedidoPct, r.TaxaServico, r.Total, troco,
                    autorizador = autorizadorId, descontos = r.Descontos.Select(d => new { d.Origem, d.Descricao, d.Valor, d.MotivoId, d.Observacao }),
                    pagamentos = pagamentos.Select(p => new { p.FormaPagamentoId, p.Valor })
                })) }, tx);

            tx.Commit();

            // NFC-e: a comanda já está fechada; falhas fiscais não desfazem o fechamento (ficam pendentes na tela Fiscal)
            Fiscal.NfceResultado? nota = null;
            string? erroDanfe = null;
            if (!r.ConsumoInterno && r.Total > 0 && nfceOpt.CurrentValue.EmitirNoFechamento)
            {
                try
                {
                    nota = await nfce.EmitirAsync(id, ct);
                    if (nota.Nova && nota.Id is not null && nota.Situacao is "AUTORIZADA" or "CONTINGENCIA")
                        erroDanfe = await FiscalEndpoints.ImprimirDanfeAsync(db, nfce, nota.Id.Value, ct);
                }
                catch (Exception e)
                {
                    nota = new Fiscal.NfceResultado("ERRO", Mensagem: "Falha ao emitir NFC-e: " + e.Message);
                }
            }
            return Results.Ok(new { id, total = r.Total, troco, descontoTotal = r.DescontoTotal, nfce = nota, erroDanfe });
        });

        // Cancela comanda inteira (ex.: aberta por engano). Com itens ativos exige quem autoriza desconto.
        g.MapPost("/comandas/{id:long}/cancelar", async (Db db, long id, CancelarComandaInput i, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(i.Motivo) || i.Motivo.Trim().Length < 3) return Erro("Informe o motivo.");
            using var c = await db.OpenAsync(ct);
            using var tx = c.BeginTransaction();
            var u = await UsuarioEndpoints.AutenticarAsync(c, i.UsuarioId, i.Pin, tx);
            if (u is null) return Erro("PIN incorreto.");
            var sit = await c.ExecuteScalarAsync<string?>("select situacao from comanda where id = :id for update", new { id }, tx);
            if (sit is null) return Results.NotFound();
            if (sit != "A") return Erro("Comanda não está aberta.");
            var ativos = await c.ExecuteScalarAsync<int>(
                "select count(*) from comanda_item where comanda_id = :id and situacao = 'A'", new { id }, tx);
            if (ativos > 0 && u.AutorizaDesconto != "S")
                return Erro("Comanda com itens: somente gerente pode cancelar.");
            await c.ExecuteAsync("""
                update comanda set situacao = 'C', fechada_em = sysdate, fechada_por = :usr, motivo_cancelamento = :motivo, valor_total = 0
                 where id = :id
                """, new { id, usr = u.Id, motivo = i.Motivo.Trim() }, tx);
            await c.ExecuteAsync("""
                insert into auditoria (usuario_id, entidade, entidade_id, acao, detalhe)
                values (:usr, 'COMANDA', :id, 'CANCELAMENTO', :detalhe)
                """, new { usr = u.Id, id = id.ToString(), detalhe = JsonSerializer.Serialize(new { motivo = i.Motivo.Trim(), itensAtivos = ativos }) }, tx);
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

    private static async Task<decimal> TaxaPadraoAsync(IDbConnection c, IDbTransaction? tx)
    {
        var v = await c.ExecuteScalarAsync<string?>("select valor from parametro where chave = 'TAXA_SERVICO_PCT'", transaction: tx);
        return decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static IResult Erro(string msg) => Results.UnprocessableEntity(new { erro = msg });
}
