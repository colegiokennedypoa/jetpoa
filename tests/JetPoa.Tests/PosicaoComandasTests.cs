using JetPoa.Api.Domain;
using JetPoa.Api.Impressao;
using Xunit;

namespace JetPoa.Tests;

public class PosicaoComandasTests
{
    [Fact]
    public void Lista_abertas_com_a_pagar_itens_e_fechadas_do_dia()
    {
        var itens = new List<Fechamento.Item>
        {
            new(1, 10, "Cerveja", 1, null, 2, 15m, null),
            new(2, 20, "Porção", 2, null, 1, 50m, null),
        };
        var calc = Fechamento.Calcular(itens, null, new Fechamento.Pedido(false, null, null, 0m), 0m);
        var conta = new Conferencia.Dados(7, "João", "12", DateTime.Today, itens, calc, DateTime.Now, PagoPrevio: 30m);
        var abertas = new List<PosicaoComandas.Aberta>
        {
            new(1, 7, "João", "51999998888", "12", "R", "Deck", DateTime.Today.AddHours(10), conta),
        };
        var fechadas = new List<PosicaoComandas.Fechada> { new(3, "Ana", DateTime.Today.AddHours(11), 42.5m, "PIX 42,50") };

        var html = System.Net.WebUtility.HtmlDecode(PosicaoComandas.Html(new DateTime(2026, 11, 13, 14, 30, 0), abertas, fechadas));

        Assert.Contains("Retrato de 13/11/2026 14:30", html);
        Assert.Contains("Comandas abertas: 1 · a receber R$ 50,00", html);   // 80 - 30 pagos antes
        Assert.Contains("(51) 99999-8888", html);
        Assert.Contains("Rio · Deck", html);
        Assert.Contains("2 x Cerveja", html);
        Assert.Contains("Pago antecipado", html);
        Assert.Contains("Fechadas hoje: 1 · R$ 42,50", html);
        Assert.Contains("PIX 42,50", html);
    }

    [Fact]
    public void Pago_antes_maior_que_consumo_mostra_credito()
    {
        var itens = new List<Fechamento.Item> { new(1, 10, "Água", 1, null, 1, 5m, null) };
        var calc = Fechamento.Calcular(itens, null, new Fechamento.Pedido(false, null, null, 0m), 0m);
        var conta = new Conferencia.Dados(9, "Bia", null, DateTime.Today, itens, calc, DateTime.Now, PagoPrevio: 100m);
        var html = System.Net.WebUtility.HtmlDecode(PosicaoComandas.Html(DateTime.Now,
            [new(1, 9, "Bia", null, null, "T", null, DateTime.Today, conta)], []));
        Assert.Contains("crédito 95,00", html);
        Assert.Contains("CRÉDITO A DEVOLVER", html);
    }

    [Fact]
    public void Sem_comandas_mostra_aviso()
    {
        var html = System.Net.WebUtility.HtmlDecode(PosicaoComandas.Html(DateTime.Now, [], []));
        Assert.Contains("Nenhuma comanda aberta.", html);
        Assert.Contains("Nenhuma comanda fechada hoje.", html);
    }
}
