// Camada: Application — porta de persistência para movimentação e saldo.
// Mantendo as operações necessárias para validar conta, registrar movimento de forma idempotente
// e calcular saldo.

using System.Threading;
using System.Threading.Tasks;
using BankMore.ContaCorrente.Domain.Entities;

namespace BankMore.ContaCorrente.Application.Movimentacao;

public interface IMovimentoRepository
{
    /// <summary>
    /// Registrando movimento de forma idempotente em uma única transação.
    /// Retornando true se persistir pela primeira vez; false se a chave já existir.
    /// Armazenando snapshots simples para auditoria/replay.
    /// </summary>
    Task<bool> TryRegistrarAsync(
        Movimento entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct);

    /// <summary>
    /// Obtendo saldo atual: soma de créditos menos soma de débitos.
    /// Retornando 0 quando não houver movimentos.
    /// </summary>
    Task<decimal> ObterSaldoAsync(string contaId, CancellationToken ct);

    /// <summary>
    /// Verificando se a conta existe.
    /// </summary>
    Task<bool> ContaExisteAsync(string contaId, CancellationToken ct);

    /// <summary>
    /// Verificando se a conta está ativa.
    /// </summary>
    Task<bool> ContaAtivaAsync(string contaId, CancellationToken ct);

    /// <summary>
    /// Obtendo o Id da conta a partir do número da conta.
    /// Retornando null se não encontrar.
    /// </summary>
    Task<string?> GetContaIdByNumeroAsync(int numero, CancellationToken ct);

    /// <summary>
    /// Obtendo o resultado armazenado na tabela de idempotência para a chave informada.
    /// Retornando null se não existir.
    /// </summary>
    Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Obtém cabeçalho da conta (número e nome) para compor a resposta do saldo.
    /// Retorna null quando não encontrar.
    /// </summary>
    Task<(int Numero, string Nome)?> GetContaCabecalhoAsync(string contaId, CancellationToken ct);
}
