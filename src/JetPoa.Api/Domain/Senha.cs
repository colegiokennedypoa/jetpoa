using System.Security.Cryptography;

namespace JetPoa.Api.Domain;

/// <summary>Hash de senha/PIN com PBKDF2-SHA256. Formato: pbkdf2$iteracoes$salt$hash (base64).</summary>
public static class Senha
{
    private const int Iteracoes = 30_000;

    public static string Hash(string segredo)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(segredo, salt, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string segredo, string? armazenado)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrEmpty(armazenado)) return false;
        var p = armazenado.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var it)) return false;
        var salt = Convert.FromBase64String(p[2]);
        var esperado = Convert.FromBase64String(p[3]);
        var calc = Rfc2898DeriveBytes.Pbkdf2(segredo, salt, it, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calc, esperado);
    }

    public static bool PinValido(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsDigit);
}
