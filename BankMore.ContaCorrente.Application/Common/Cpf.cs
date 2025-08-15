// Camada: Application (utilitário de domínio próximo).
// Responsabilidade: sanitizar (manter só dígitos) e validar CPF (cálculo dos dígitos verificadores).
// Observações importantes para o desafio:
// - O CPF será usado APENAS dentro do microsserviço de Conta Corrente (exigência de segurança).
// - Outros serviços nunca devem trafegá-lo; usaremos o id da conta (GUID) no JWT entre serviços.

namespace BankMore.ContaCorrente.Application.Common;

public static class Cpf
{
    /// <summary>
    /// Remove todos os caracteres não numéricos. Ex.: "123.456.789-09" -> "12345678909".
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var arr = new char[input.Length];
        var j = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c >= '0' && c <= '9') arr[j++] = c;
        }
        return new string(arr, 0, j);
    }

    /// <summary>
    /// Valida o CPF aplicando o algoritmo oficial dos dígitos verificadores.
    /// Regras:
    /// - Deve conter 11 dígitos após sanitize;
    /// - Não pode ser uma sequência de um mesmo dígito (ex.: "00000000000");
    /// - DV1 calculado com pesos 10..2; DV2 com pesos 11..2.
    /// </summary>
    public static bool IsValid(string? input)
    {
        var cpf = Sanitize(input);
        if (cpf.Length != 11) return false;

        // Rejeita sequências (todos dígitos iguais)
        bool allEqual = true;
        for (int i = 1; i < 11 && allEqual; i++)
            if (cpf[i] != cpf[0]) allEqual = false;
        if (allEqual) return false;

        // Converte para inteiros
        Span<int> d = stackalloc int[11];
        for (int i = 0; i < 11; i++)
        {
            int digit = cpf[i] - '0';
            if (digit < 0 || digit > 9) return false;
            d[i] = digit;
        }

        // Calcula DV1 (pesos 10..2)
        int sum = 0;
        for (int i = 0, peso = 10; i < 9; i++, peso--)
            sum += d[i] * peso;
        int dv1 = 11 - (sum % 11);
        if (dv1 >= 10) dv1 = 0;
        if (d[9] != dv1) return false;

        // Calcula DV2 (pesos 11..2)
        sum = 0;
        for (int i = 0, peso = 11; i < 10; i++, peso--)
            sum += d[i] * peso;
        int dv2 = 11 - (sum % 11);
        if (dv2 >= 10) dv2 = 0;
        if (d[10] != dv2) return false;

        return true;
    }

    /// <summary>
    /// Tenta normalizar: retorna true se válido e devolve o CPF "limpo" (11 dígitos) em <paramref name="normalized"/>.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = Sanitize(input);
        if (!IsValid(normalized))
        {
            normalized = string.Empty;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Aplica máscara "000.000.000-00" a um CPF de 11 dígitos (não valida).
    /// Útil para exibir no Swagger/Logs (se necessário).
    /// </summary>
    public static string ToMasked(string cpf11)
    {
        var s = Sanitize(cpf11);
        if (s.Length != 11) return s;
        return $"{s.Substring(0, 3)}.{s.Substring(3, 3)}.{s.Substring(6, 3)}-{s.Substring(9, 2)}";
    }
}
