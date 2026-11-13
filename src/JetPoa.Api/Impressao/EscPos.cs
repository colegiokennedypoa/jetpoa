using System.Globalization;
using System.Text;

namespace JetPoa.Api.Impressao;

/// <summary>
/// Monta um ticket para impressora térmica em uma de duas linguagens de comando:
/// ESC/POS (Epson-compatível: G250, Bematech em modo ESC/POS) ou ESC/Bema (modo nativo da Bematech MP-4200).
/// No modo errado a impressora imprime os comandos como texto ("!" e "VB" no papel).
/// </summary>
public sealed class EscPos
{
    public const string ProtocoloEscPos = "ESCPOS";
    public const string ProtocoloBema = "BEMA";

    private static readonly Encoding Cp850;
    private readonly MemoryStream _buf = new();
    private readonly bool _removerAcentos;

    public int Colunas { get; }
    /// <summary>true = ESC/Bema (Bematech nativo); false = ESC/POS.</summary>
    public bool Bema { get; }

    static EscPos()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp850 = Encoding.GetEncoding(850);
    }

    public EscPos(int colunas = 48, bool removerAcentos = true, string? protocolo = null)
    {
        Colunas = colunas;
        _removerAcentos = removerAcentos;
        Bema = string.Equals(protocolo, ProtocoloBema, StringComparison.OrdinalIgnoreCase);
        Raw(0x1B, 0x40);                    // ESC @  inicializa (igual nos dois)
        if (!Bema) Raw(0x1B, 0x74, 0x02);   // ESC t 2  tabela PC850 (ESC/POS)
    }

    public EscPos Raw(params byte[] b) { _buf.Write(b); return this; }

    // ESC a n: alinhamento, igual nos dois
    public EscPos Centro() => Raw(0x1B, 0x61, 0x01);
    public EscPos Esquerda() => Raw(0x1B, 0x61, 0x00);

    /// <summary>ESC/POS: ESC E n. ESC/Bema: ESC E liga, ESC F desliga (sem parâmetro).</summary>
    public EscPos Negrito(bool on) => Bema ? Raw(0x1B, (byte)(on ? 0x45 : 0x46)) : Raw(0x1B, 0x45, (byte)(on ? 1 : 0));

    /// <summary>
    /// Tamanho da letra: 0 normal, 0x01 altura dupla, 0x11 altura e largura duplas.
    /// ESC/POS: GS ! n. ESC/Bema: ESC W n (largura dupla) e ESC d n (altura dupla).
    /// </summary>
    public EscPos Tamanho(byte n)
    {
        if (!Bema) return Raw(0x1D, 0x21, n);
        var largura = (n & 0xF0) != 0;
        var altura = (n & 0x0F) != 0;
        return Raw(0x1B, 0x57, (byte)(largura ? 1 : 0)).Raw(0x1B, 0x64, (byte)(altura ? 1 : 0));
    }

    /// <summary>Avança linhas. ESC/POS: ESC d n (no ESC/Bema o ESC d é altura dupla, então usa quebras de linha).</summary>
    public EscPos Avancar(byte linhas)
    {
        if (!Bema) return Raw(0x1B, 0x64, linhas);
        for (var i = 0; i < linhas; i++) _buf.WriteByte(0x0A);
        return this;
    }

    /// <summary>Corte parcial. ESC/POS: GS V B 0 (avança até a guilhotina). ESC/Bema: ESC m.</summary>
    public EscPos Cortar() => Bema ? Raw(0x1B, 0x6D) : Raw(0x1D, 0x56, 0x42, 0x00);

    public EscPos Texto(string s)
    {
        if (_removerAcentos) s = SemAcentos(s);
        _buf.Write(Cp850.GetBytes(s));
        return this;
    }

    public EscPos Linha(string s = "") => Texto(s + "\n");
    public EscPos Separador(char c = '-') => Linha(new string(c, Colunas));

    /// <summary>Quebra o texto em linhas de no máximo <paramref name="largura"/> colunas.</summary>
    public static IEnumerable<string> Quebrar(string texto, int largura)
    {
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var atual = new StringBuilder();
        foreach (var p in palavras)
        {
            var palavra = p;
            while (palavra.Length > largura)
            {
                if (atual.Length > 0) { yield return atual.ToString(); atual.Clear(); }
                yield return palavra[..largura];
                palavra = palavra[largura..];
            }
            if (atual.Length + palavra.Length + (atual.Length > 0 ? 1 : 0) > largura)
            {
                yield return atual.ToString();
                atual.Clear();
            }
            if (atual.Length > 0) atual.Append(' ');
            atual.Append(palavra);
        }
        if (atual.Length > 0) yield return atual.ToString();
    }

    public static string SemAcentos(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public byte[] ToArray() => _buf.ToArray();
}
