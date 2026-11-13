using JetPoa.Api.Impressao;
using Xunit;

namespace JetPoa.Tests;

public class EscPosTests
{
    private static bool Contem(byte[] b, params byte[] seq)
    {
        for (var i = 0; i + seq.Length <= b.Length; i++)
            if (b.AsSpan(i, seq.Length).SequenceEqual(seq)) return true;
        return false;
    }

    [Fact]
    public void EscPos_usa_GS_para_tamanho_e_corte()
    {
        var b = new EscPos(48, true, "ESCPOS").Tamanho(0x11).Linha("X").Tamanho(0).Avancar(4).Cortar().ToArray();
        Assert.True(Contem(b, 0x1D, 0x21, 0x11));        // GS ! 0x11
        Assert.True(Contem(b, 0x1B, 0x64, 0x04));        // ESC d 4 (avança)
        Assert.True(Contem(b, 0x1D, 0x56, 0x42, 0x00));  // GS V B 0
    }

    [Fact]
    public void Bema_nao_manda_comandos_GS_que_a_MP4200_imprime_como_texto()
    {
        var b = new EscPos(48, true, "BEMA").Negrito(true).Tamanho(0x11).Linha("MESA 11").Tamanho(0).Negrito(false)
            .Avancar(4).Cortar().ToArray();
        Assert.DoesNotContain((byte)0x1D, b);            // nenhum GS (virava "!" e "VB" no papel)
        Assert.True(Contem(b, 0x1B, 0x57, 0x01));        // ESC W 1 largura dupla
        Assert.True(Contem(b, 0x1B, 0x64, 0x01));        // ESC d 1 altura dupla
        Assert.True(Contem(b, 0x1B, 0x45));              // ESC E negrito
        Assert.True(Contem(b, 0x1B, 0x46));              // ESC F fim do negrito
        Assert.True(Contem(b, 0x0A, 0x0A, 0x0A, 0x0A));  // avanço por quebras de linha
        Assert.True(Contem(b, 0x1B, 0x6D));              // ESC m corte parcial
    }

    [Fact]
    public void Ticket_de_teste_informa_a_linguagem()
    {
        var texto = System.Text.Encoding.Latin1.GetString(ImpressaoProducao.TicketTeste("Garçom", 48, true, "BEMA"));
        Assert.Contains("ESC/Bema", texto);
    }
}
