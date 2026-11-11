namespace JetPoa.Api.Data;

/// <summary>
/// Lê um arquivo local "Chave=Valor" (ex.: C:\Projeto\certificado\52822987000123.conf) com segredos
/// que não podem ir para o Git (conexão, senha do certificado, CSC). "__" separa níveis, como nas
/// variáveis de ambiente (Nfce__Csc = Nfce:Csc). Linhas vazias e iniciadas por # são ignoradas.
/// </summary>
public static class ArquivoConf
{
    public static IConfigurationBuilder AddArquivoConf(this IConfigurationBuilder builder, string? caminho, ILogger? log = null)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return builder;
        if (!File.Exists(caminho))
        {
            log?.LogWarning("Arquivo de configuração {Caminho} não encontrado.", caminho);
            return builder;
        }

        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var bruta in File.ReadAllLines(caminho))
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith('#')) continue;
            var i = linha.IndexOf('=');
            if (i <= 0) continue;
            var chave = linha[..i].Trim().Replace("__", ":");
            var valor = linha[(i + 1)..].Trim();
            if (valor.Length > 0) valores[chave] = valor;   // valor vazio não sobrescreve
        }
        return builder.AddInMemoryCollection(valores);
    }
}
