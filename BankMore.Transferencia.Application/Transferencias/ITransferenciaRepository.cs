// Camada: Application — porta de persistência da Transferência.
// Adicionando operações para iniciar idempotência e concluir após orquestração.

using System.Threading;
using System.Threading.Tasks;
using Entity = BankMore.Transferencia.Domain.Entities;

namespace BankMore.Transferencia.Application.Transferencias;

public interface ITransferenciaRepository
{
    /// <summary>
    /// Iniciando idempotência para a chave informada.
    /// Retornando false quando a chave já existir.
    /// </summary>
    Task<bool> TryBeginIdempotentAsync(
        string idempotencyKey,
        string requisicaoJson,
        CancellationToken ct);

    /// <summary>
    /// Concluindo a transferência após sucesso das chamadas externas.
    /// Gravando a entidade em "transferencia" e atualizando "idempotencia.resultado".
    /// </summary>
    Task CompleteAsync(
        Entity.Transferencia entity,
        string idempotencyKey,
        string resultadoJson,
        CancellationToken ct);

    /// <summary>
    /// Obtendo o resultado armazenado para a chave de idempotência.
    /// </summary>
    Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Registrando transferência e idempotência em uma única transação.
    /// Mantendo para compatibilidade.
    /// </summary>
    Task<bool> TryRegisterAsync(
        Entity.Transferencia entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct);
}
