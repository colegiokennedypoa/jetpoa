using JetPoa.Api.Domain;
using Xunit;
using static JetPoa.Api.Domain.Fechamento;

namespace JetPoa.Tests;

/// <summary>Mesmos cenários validados na API durante a Fase 3.</summary>
public class FechamentoTests
{
    private static readonly List<Item> Itens =
    [
        new(1, 10, "HEINEKEN 330 ML", 2, null, 2, 18m, 6.49m),
        new(2, 11, "FRITAS 400 G", 12, null, 1, 26m, 8m),
        new(3, 12, "ESTACIONAMENTO", 1, "ESTACIONAMENTO", 1, 20m, null),
    ];

    private static readonly CategoriaCliente Vip = new("VIP", 0, true, true, true, false);

    [Fact]
    public void Vip_isenta_estacionamento_sem_contar_na_alcada()
    {
        var r = Calcular(Itens, Vip, new Pedido(false, null, null, null), 0);
        Assert.Equal(82m, r.Subtotal);
        Assert.Equal(20m, r.DescontoTotal);
        Assert.Equal(0m, r.DescontoConcedido);
        Assert.Equal(62m, r.Total);
    }

    [Fact]
    public void Desconto_10pct_rateado_por_item()
    {
        var r = Calcular(Itens, Vip, new Pedido(false, null, [new("TOTAL", null, null, "P", 10, 1, null)], null), 0);
        Assert.Equal(6.20m, r.DescontoConcedido);
        Assert.Equal(7.56m, r.DescontoConcedidoPct);
        Assert.Equal(55.80m, r.Total);
        Assert.Equal(3.60m, r.DescontoPorItem[1]);
        Assert.Equal(2.60m, r.DescontoPorItem[2]);
        Assert.Equal(20m, r.DescontoPorItem[3]);
        Assert.Equal(r.DescontoTotal, r.DescontoPorItem.Values.Sum());
    }

    [Fact]
    public void Taxa_de_servico_so_sobre_consumo()
    {
        var itens = Itens.Where(i => i.Id != 1).ToList();
        var r = Calcular(itens, null, new Pedido(false, null, [new("ITEM", 2, null, "V", 2, 1, null)], 10), 0);
        Assert.Equal(2.40m, r.TaxaServico);          // 10% de (26 - 2); estacionamento fora
        Assert.Equal(46.40m, r.Total);
        Assert.Equal(4.35m, r.DescontoConcedidoPct);
    }

    [Fact]
    public void Consumo_interno_preco_de_custo()
    {
        var r = Calcular([Itens[0]], null, new Pedido(true, null, null, 10), 0);
        Assert.Equal(12.98m, r.Total);
        Assert.Equal(0m, r.TaxaServico);
    }

    [Fact]
    public void Desconto_em_reais_nao_passa_do_saldo_e_centavos_fecham()
    {
        var r = Calcular(Itens, null, new Pedido(false, null, [new("TOTAL", null, null, "V", 1000, 1, null)], null), 0);
        Assert.Equal(62m, r.DescontoConcedido);       // só consumo (taxa fica fora do desconto total)
        Assert.Equal(20m, r.Total);
        var r2 = Calcular(Itens, null, new Pedido(false, null, [new("TOTAL", null, null, "P", 33.33m, 1, null)], null), 0);
        Assert.Equal(r2.DescontoTotal, r2.DescontoPorItem.Values.Sum());
    }

    [Fact]
    public void Desconto_sem_motivo_gera_erro()
    {
        var r = Calcular(Itens, null, new Pedido(false, null, [new("TOTAL", null, null, "P", 10, null, null)], null), 0);
        Assert.NotEmpty(r.Erros);
    }

    [Theory]
    [InlineData("52998224725", true)]
    [InlineData("11111111111", false)]
    [InlineData("12345678900", false)]
    public void Cpf(string cpf, bool ok) => Assert.Equal(ok, Documento.CpfValido(cpf));

    [Theory]
    [InlineData("52822987000123", true)]
    [InlineData("52822987000124", false)]
    public void Cnpj(string cnpj, bool ok) => Assert.Equal(ok, Documento.CnpjValido(cnpj));
}
