// Camada: Application (DDD) — "Port" de persistência para o caso de uso de Transferência.
// Ideia: a Application depende de uma interface; a Infrastructure fornece a implementação (Dapper).
// Aqui modelamos a operação como IDÊMPOTENTE: para a mesma IdempotencyKey, sempre
// retornaremos o MESMO efeito/resultado, sem duplicar gravações.

using TransferenciaDomain = BankMore.Transferencia.Domain.Entities;

namespace BankMore.Transferencia.Application.Transferencias;

public interface ITransferenciaRepository
{
    /// <summary>
    /// Tenta registrar uma transferência de forma IDÊMPOTENTE.
    /// Regra esperada (a ser implementada na Infra com transação):
    /// 1) Se a chave de idempotência JÁ EXISTE -> NÃO grava novamente e retorna false.
    /// 2) Se NÃO EXISTE -> grava a transferência e insere a chave em idempotencia -> retorna true.
    /// 
    /// Observações:
    /// - <paramref name="requisicaoJson"/> e <paramref name="resultadoJson"/> são armazenados para auditoria e replay.
    /// - A DataMovimento e o Valor vêm do objeto de domínio (Transferencia).
    /// - A implementação deve ser atômica (uma transação envolvendo as duas tabelas).
    /// </summary>
    /// <param name="entity">Entidade de domínio com os dados da transferência</param>
    /// <param name="idempotencyKey">Chave idempotente única por requisição</param>
    /// <param name="requisicaoJson">Snapshot JSON da requisição (para auditoria)</param>
    /// <param name="resultadoJson">Snapshot JSON do resultado (para replay)</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>
    /// true  -> primeira execução (persistiu transferência e chave de idempotência);
    /// false -> chave já processada (NÃO reexecutar; devolva o mesmo resultado armazenado).
    /// </returns>
    Task<bool> TryRegisterAsync(
        TransferenciaDomain.Transferencia entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct);

    /// <summary>
    /// Retorna o "resultado" previamente armazenado para a chave de idempotência,
    /// permitindo responder de forma consistente em replays.
    /// </summary>
    /// <param name="idempotencyKey">Chave idempotente</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>JSON do resultado ou null se não existir</returns>
    Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);
}
