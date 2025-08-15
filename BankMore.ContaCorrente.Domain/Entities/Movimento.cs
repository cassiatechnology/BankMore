// Camada: Domain — entidade de Movimento (C ou D) da conta corrente.
// Mantendo DataMovimento como "DD/MM/YYYY" para compatibilidade com o schema.
// Garantindo regras mínimas aqui: tipo válido e valor positivo.

namespace BankMore.ContaCorrente.Domain.Entities;

public sealed record Movimento(
    string IdMovimento,         // GUID em string
    string IdContaCorrente,     // referência à conta (GUID em string)
    string DataMovimento,       // "DD/MM/YYYY" (UTC formatado)
    char TipoMovimento,       // 'C' = Crédito, 'D' = Débito
    decimal Valor               // 2 casas decimais
)
{
    /// <summary>
    /// Criando um Movimento validado (tipo C/D e valor > 0). Formatando data para "DD/MM/YYYY".
    /// </summary>
    public static Movimento Create(
        string idContaCorrente,
        DateTime whenUtc,
        char tipo,        // 'C' ou 'D'
        decimal valor)
    {
        if (string.IsNullOrWhiteSpace(idContaCorrente))
            throw new ArgumentException("Id da conta é obrigatório.", nameof(idContaCorrente));

        tipo = char.ToUpperInvariant(tipo);
        if (tipo != 'C' && tipo != 'D')
            throw new ArgumentException("Tipo inválido. Use 'C' ou 'D'.", nameof(tipo));

        if (valor <= 0m)
            throw new ArgumentOutOfRangeException(nameof(valor), "Valor deve ser positivo.");

        var id = Guid.NewGuid().ToString();
        var data = whenUtc.ToUniversalTime().ToString("dd'/'MM'/'yyyy");
        var valor2 = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

        return new Movimento(
            IdMovimento: id,
            IdContaCorrente: idContaCorrente,
            DataMovimento: data,
            TipoMovimento: tipo,
            Valor: valor2
        );
    }
}
