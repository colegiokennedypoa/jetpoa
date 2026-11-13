using JetPoa.Api.Domain;
using JetPoa.Api.Impressao;
using Xunit;

namespace JetPoa.Tests;

public class ConferenciaTests
{
    [Fact]
    public void Agrupa_itens_e_mostra_taxa_e_total()
    {
        var itens = new List<Fechamento.Item>
        {
            new(1, 10, "Coca-Cola Lata", 1, null, 1, 7m, null),
            new(2, 10, "Coca-Cola Lata", 1, null, 2, 7m, null),
            new(3, 20, "Porção de Fritas", 2, null, 1, 35m, null),
        };
        var calc = Fechamento.Calcular(itens, null, new Fechamento.Pedido(false, null, null, null), 10m);
        var html = System.Net.WebUtility.HtmlDecode(
            Conferencia.Html(new Conferencia.Dados(12, "Maria", "Deck 3", DateTime.Today, itens, calc, DateTime.Now)));

        Assert.Contains("COMANDA 12", html);
        Assert.Contains("NÃO É DOCUMENTO FISCAL", html);
        Assert.Contains("Mesa Deck 3", html);
        Assert.Contains("3x Coca-Cola Lata", html);
        Assert.Contains("21,00", html);
        Assert.Contains("1x Porção de Fritas", html);
        Assert.Contains("Taxa de serviço 10%", html);
        Assert.Contains(calc.Total.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), html);
    }

    [Fact]
    public void Pagamento_previo_aparece_e_mostra_so_a_diferenca()
    {
        var itens = new List<Fechamento.Item> { new(1, 10, "Almoço", 1, null, 1, 100m, null) };
        var calc = Fechamento.Calcular(itens, null, new Fechamento.Pedido(false, null, null, 0m), 0m);
        var html = System.Net.WebUtility.HtmlDecode(
            Conferencia.Html(new Conferencia.Dados(5, null, null, DateTime.Today, itens, calc, DateTime.Now, PagoPrevio: 60m)));
        Assert.Contains("Pago antecipado", html);
        Assert.Contains("-60,00", html);
        Assert.Contains("A PAGAR R$", html);
        Assert.Contains("40,00", html);
    }
}
