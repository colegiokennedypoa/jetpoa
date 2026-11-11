using System.Globalization;
using System.Text;

namespace JetPoa.Api.Impressao;

/// <summary>Monta um ticket ESC/POS (Epson-compatível: Bematech MP-4200 em modo ESC/POS, G250 etc.).</summary>
public sealed class EscPos
{
    private static readonly Encoding Cp850;
    private readonly MemoryStream _buf = new();
    private readonly bool _removerAcentos;

    public int Colunas { get; }

    static EscPos()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp850 = Encoding.GetEncoding(850);
    }

    public EscPos(int colunas = 48, bool removerAcentos = true)
    {
        Colunas = colunas;
        _removerAcentos = removerAcentos;
        Raw(0x1B, 0x40);       // ESC @  inicializa
        Raw(0x1B, 0x74, 0x02); // ESC t 2  tabela PC850 (multilíngue, tem acentos PT)
    }

    public EscPos Raw(params byte[] b) { _buf.Write(b); return this; }

    public EscPos Centro() => Raw(0x1B, 0x61, 0x01);
    public EscPos Esquerda() => Raw(0x1B, 0x61, 0x00);
    public EscPos Negrito(bool on) => Raw(0x1B, 0x45, (byte)(on ? 1 : 0));
    /// <summary>GS ! n — 0 normal, 0x01 altura dupla, 0x11 altura e largura duplas.</summary>
    public EscPos Tamanho(byte n) => Raw(0x1D, 0x21, n);
    public EscPos Avancar(byte linhas) => Raw(0x1B, 0x64, linhas);
    /// <summary>GS V B 0 — avança e faz corte parcial.</summary>
    public EscPos Cortar() => Raw(0x1D, 0x56, 0x42, 0x00);

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
