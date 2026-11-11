namespace JetPoa.Api.Domain;

public static class Documento
{
    public static string? SoDigitos(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : new string(valor.Where(char.IsDigit).ToArray());

    /// <summary>Valida CPF (11 dígitos, dígitos verificadores, rejeita sequências repetidas).</summary>
    public static bool CpfValido(string? cpf)
    {
        cpf = SoDigitos(cpf);
        if (cpf is null || cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;

        int Dv(int len)
        {
            var soma = 0;
            for (var i = 0; i < len; i++) soma += (cpf[i] - '0') * (len + 1 - i);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Dv(9) == cpf[9] - '0' && Dv(10) == cpf[10] - '0';
    }

    /// <summary>Valida CNPJ numérico (14 dígitos).</summary>
    public static bool CnpjValido(string? cnpj)
    {
        cnpj = SoDigitos(cnpj);
        if (cnpj is null || cnpj.Length != 14 || cnpj.Distinct().Count() == 1) return false;

        int Dv(int len)
        {
            int[] pesos = len == 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            var soma = 0;
            for (var i = 0; i < len; i++) soma += (cnpj[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Dv(12) == cnpj[12] - '0' && Dv(13) == cnpj[13] - '0';
    }
}
