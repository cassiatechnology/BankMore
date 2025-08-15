namespace BankMore.Transferencia.Domain.Entities;

public sealed record Transferencia(
    string IdTransferencia,        // TEXT(37) no SQLite — aqui usamos GUID em string
    string IdContaCorrenteOrigem,  // TEXT(37)
    string IdContaCorrenteDestino, // TEXT(37)
    string DataMovimento,          // "DD/MM/YYYY" (alinhado ao script/sql)
    decimal Valor                  // valor da transferência (2 casas na prática)
)
{
    /// <summary>
    /// Fábrica de domínio: cria uma nova Transferência garantindo formato de data e arredondamento do valor.
    /// Conceito DDD: regras coesas no domínio, evitando leaks de detalhes de transporte/infra.
    /// </summary>
    public static Transferencia CreateNew(
        string contaOrigemId,
        string contaDestinoId,
        DateTime whenUtc,
        decimal valor)
    {
        if (string.IsNullOrWhiteSpace(contaOrigemId))
            throw new ArgumentException("Id da conta de origem é obrigatório.", nameof(contaOrigemId));
        if (string.IsNullOrWhiteSpace(contaDestinoId))
            throw new ArgumentException("Id da conta de destino é obrigatório.", nameof(contaDestinoId));
        if (valor <= 0)
            throw new ArgumentOutOfRangeException(nameof(valor), "Valor deve ser positivo.");

        // Formata a data como "DD/MM/YYYY" (pedido do script). Mantemos em UTC por consistência.
        var dataMov = whenUtc.ToUniversalTime().ToString("dd'/'MM'/'yyyy");

        // Gera o identificador no padrão usado pelo script (GUID em string cabe no TEXT(37))
        var id = Guid.NewGuid().ToString();

        // Arredonda para 2 casas (negócio monetário; a infra cuidará da persistência no tipo REAL do SQLite)
        var valor2 = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

        return new Transferencia(
            IdTransferencia: id,
            IdContaCorrenteOrigem: contaOrigemId,
            IdContaCorrenteDestino: contaDestinoId,
            DataMovimento: dataMov,
            Valor: valor2
        );
    }
}
