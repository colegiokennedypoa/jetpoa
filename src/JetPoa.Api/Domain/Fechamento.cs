namespace JetPoa.Api.Domain;

/// <summary>
/// Cálculo do fechamento da comanda (função pura, usada na simulação e no fechamento).
///
/// Ordem de aplicação, sempre sobre o saldo restante de cada item:
///   1. Consumo interno: item passa a valer o preço de custo (exige autorização).
///   2. Isenções automáticas pela categoria do cliente (estacionamento, embarque, couvert).
///   3. Desconto padrão da categoria do cliente (%), exceto nas taxas.
///   4. Isenções manuais de taxa.
///   5. Descontos manuais (item, categoria ou total; % ou R$).
/// Taxa de serviço incide sobre o saldo dos itens que não são taxa.
/// A alçada considera apenas o que o operador concedeu (passos 4 e 5) e o consumo interno.
/// </summary>
public static class Fechamento
{
    public sealed record Item(long Id, long ProdutoId, string Produto, long CategoriaId, string? TaxaTipo,
                              decimal Quantidade, decimal PrecoUnitario, decimal? PrecoCusto);

    public sealed record CategoriaCliente(string Nome, decimal DescontoPct, bool IsentaEstacionamento,
                                          bool IsentaEmbarque, bool IsentaCouvert, bool IsentaTaxaServico);

    public sealed record DescontoManual(string Escopo, long? ItemId, long? CategoriaId, string Tipo,
                                        decimal Valor, long? MotivoId, string? Observacao);

    public sealed record Pedido(bool ConsumoInterno, List<string>? IsentarTaxas, List<DescontoManual>? Descontos,
                                decimal? TaxaServicoPct);

    public sealed record DescontoCalculado(string Origem, string Escopo, long? ItemId, long? CategoriaId, string? TaxaTipo,
                                           string Tipo, decimal Informado, decimal Valor, long? MotivoId,
                                           string? Observacao, string Descricao);

    public sealed record Resultado(
        decimal Subtotal, decimal DescontoTotal, decimal DescontoConcedido, decimal DescontoConcedidoPct,
        decimal TaxaServicoPct, decimal TaxaServicoBase, decimal TaxaServico, decimal Total,
        bool ConsumoInterno, List<DescontoCalculado> Descontos, Dictionary<long, decimal> DescontoPorItem,
        List<string> Erros);

    public static Resultado Calcular(IReadOnlyList<Item> itens, CategoriaCliente? cat, Pedido p, decimal taxaServicoPadraoPct)
    {
        var erros = new List<string>();
        var bruto = itens.ToDictionary(i => i.Id, i => R(i.Quantidade * i.PrecoUnitario));
        var saldo = new Dictionary<long, decimal>(bruto);
        var descontos = new List<DescontoCalculado>();
        var subtotal = bruto.Values.Sum();

        // 1. consumo interno
        if (p.ConsumoInterno)
        {
            foreach (var i in itens)
            {
                var custo = i.PrecoCusto is > 0 ? Math.Min(i.PrecoCusto.Value, i.PrecoUnitario) : i.PrecoUnitario;
                var d = R(i.Quantidade * (i.PrecoUnitario - custo));
                if (d > 0) saldo[i.Id] -= d;
            }
            var total = subtotal - saldo.Values.Sum();
            if (total > 0)
                descontos.Add(new("INTERNO", "TOTAL", null, null, null, "V", total, total, null, null, "Consumo interno (preço de custo)"));
        }
        else
        {
            // 2. isenções automáticas da categoria
            if (cat is not null)
            {
                foreach (var (taxa, isenta) in new[] {
                    ("ESTACIONAMENTO", cat.IsentaEstacionamento), ("EMBARQUE", cat.IsentaEmbarque), ("COUVERT", cat.IsentaCouvert) })
                {
                    if (!isenta) continue;
                    var v = Zerar(itens.Where(i => i.TaxaTipo == taxa), saldo);
                    if (v > 0) descontos.Add(new("CATEGORIA", "TAXA", null, null, taxa, "P", 100, v, null, null,
                        $"Isenção de {Nome(taxa)} ({cat.Nome})"));
                }

                // 3. desconto padrão da categoria
                if (cat.DescontoPct > 0)
                {
                    var alvo = itens.Where(i => i.TaxaTipo is null).ToList();
                    var v = Ratear(alvo, saldo, R(alvo.Sum(i => saldo[i.Id]) * cat.DescontoPct / 100));
                    if (v > 0) descontos.Add(new("CATEGORIA", "TOTAL", null, null, null, "P", cat.DescontoPct, v, null, null,
                        $"Desconto {cat.Nome} {cat.DescontoPct:0.##}%"));
                }
            }

            // 4. isenções manuais de taxa
            foreach (var taxa in (p.IsentarTaxas ?? []).Select(t => t.ToUpperInvariant()).Distinct())
            {
                var v = Zerar(itens.Where(i => i.TaxaTipo == taxa), saldo);
                if (v > 0) descontos.Add(new("ISENCAO", "TAXA", null, null, taxa, "P", 100, v, null, null, $"Isenção de {Nome(taxa)}"));
            }

            // 5. descontos manuais
            foreach (var d in p.Descontos ?? [])
            {
                var escopo = (d.Escopo ?? "TOTAL").ToUpperInvariant();
                var tipo = (d.Tipo ?? "P").ToUpperInvariant();
                List<Item> alvo = escopo switch
                {
                    "ITEM" => itens.Where(i => i.Id == d.ItemId).ToList(),
                    "CATEGORIA" => itens.Where(i => i.CategoriaId == d.CategoriaId && i.TaxaTipo is null).ToList(),
                    _ => itens.Where(i => i.TaxaTipo is null).ToList(),
                };
                if (alvo.Count == 0) { erros.Add($"Desconto ({escopo}) sem itens correspondentes."); continue; }
                if (d.Valor <= 0 || (tipo == "P" && d.Valor > 100)) { erros.Add("Valor de desconto inválido."); continue; }
                if (d.MotivoId is null) { erros.Add("Todo desconto precisa de motivo."); continue; }

                var baseCalc = alvo.Sum(i => saldo[i.Id]);
                var pedido = tipo == "P" ? R(baseCalc * d.Valor / 100) : Math.Min(R(d.Valor), baseCalc);
                var v = Ratear(alvo, saldo, pedido);
                var rotulo = escopo switch
                {
                    "ITEM" => alvo[0].Produto,
                    "CATEGORIA" => "categoria",
                    _ => "total"
                };
                if (v > 0) descontos.Add(new("MANUAL", escopo, d.ItemId, d.CategoriaId, null, tipo, d.Valor, v, d.MotivoId,
                    string.IsNullOrWhiteSpace(d.Observacao) ? null : d.Observacao.Trim(),
                    tipo == "P" ? $"Desconto {d.Valor:0.##}% em {rotulo}" : $"Desconto de {d.Valor:C} em {rotulo}"));
            }
        }

        var descontoTotal = subtotal - saldo.Values.Sum();
        var concedido = descontos.Where(d => d.Origem is "MANUAL" or "ISENCAO" or "INTERNO").Sum(d => d.Valor);
        var concedidoPct = subtotal > 0 ? Math.Round(concedido / subtotal * 100, 2) : 0;

        // taxa de serviço
        // categoria isenta só zera a taxa quando o operador não informou outro percentual
        var pct = p.ConsumoInterno || ((cat?.IsentaTaxaServico ?? false) && p.TaxaServicoPct is null)
            ? 0
            : p.TaxaServicoPct ?? taxaServicoPadraoPct;
        if (pct is < 0 or > 20) { erros.Add("Taxa de serviço deve estar entre 0 e 20%."); pct = 0; }
        var baseTaxa = itens.Where(i => i.TaxaTipo is null).Sum(i => saldo[i.Id]);
        var valorServico = R(baseTaxa * pct / 100);

        var porItem = itens.ToDictionary(i => i.Id, i => bruto[i.Id] - saldo[i.Id]);
        return new Resultado(subtotal, descontoTotal, concedido, concedidoPct, pct, baseTaxa, valorServico,
            subtotal - descontoTotal + valorServico, p.ConsumoInterno, descontos, porItem, erros);
    }

    /// <summary>Zera o saldo dos itens; devolve o valor isentado.</summary>
    private static decimal Zerar(IEnumerable<Item> alvo, Dictionary<long, decimal> saldo)
    {
        decimal total = 0;
        foreach (var i in alvo) { total += saldo[i.Id]; saldo[i.Id] = 0; }
        return total;
    }

    /// <summary>Distribui um desconto proporcionalmente ao saldo; resíduo de centavos vai para o maior item.</summary>
    private static decimal Ratear(List<Item> alvo, Dictionary<long, decimal> saldo, decimal valor)
    {
        var baseTotal = alvo.Sum(i => saldo[i.Id]);
        if (valor <= 0 || baseTotal <= 0) return 0;
        valor = Math.Min(valor, baseTotal);
        decimal aplicado = 0;
        foreach (var i in alvo)
        {
            var parte = Math.Min(saldo[i.Id], R(valor * saldo[i.Id] / baseTotal));
            saldo[i.Id] -= parte;
            aplicado += parte;
        }
        var resto = valor - aplicado;
        foreach (var i in alvo.OrderByDescending(i => saldo[i.Id]))
        {
            if (resto == 0) break;
            var ajuste = resto > 0 ? Math.Min(resto, saldo[i.Id]) : resto;
            saldo[i.Id] -= ajuste;
            resto -= ajuste;
        }
        return valor - resto;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string Nome(string taxa) => taxa switch
    {
        "ESTACIONAMENTO" => "estacionamento",
        "EMBARQUE" => "embarque",
        "COUVERT" => "couvert",
        _ => taxa.ToLowerInvariant()
    };
}
