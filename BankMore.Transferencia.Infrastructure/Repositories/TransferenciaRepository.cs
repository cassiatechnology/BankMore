// Camada: Infrastructure — implementação Dapper da porta ITransferenciaRepository.
// Aplica Ports & Adapters: a Application conhece a interface; aqui faço o acesso ao SQLite.
// Mantém transação onde necessário e idempotência via tabela `idempotencia`.

using BankMore.Transferencia.Application.Transferencias;
using Dapper;
using System.Data;
using Entity = BankMore.Transferencia.Domain.Entities;

namespace BankMore.Transferencia.Infrastructure.Repositories;

public sealed class TransferenciaRepository : ITransferenciaRepository
{
    private readonly IDbConnection _conn;

    public TransferenciaRepository(IDbConnection conn) => _conn = conn;

    /// <summary>
    /// Inicia idempotência. Insere a chave se ainda não existir.
    /// Retorna true na primeira execução; false no replay.
    /// </summary>
    public async Task<bool> TryBeginIdempotentAsync(
        string idempotencyKey,
        string requisicaoJson,
        CancellationToken ct)
    {
        EnsureOpen();

        const string sql = @"
INSERT OR IGNORE INTO idempotencia (chave_idempotencia, requisicao, resultado)
VALUES (@Key, @Req, NULL);";

        var affected = await _conn.ExecuteAsync(
            new CommandDefinition(sql, new { Key = idempotencyKey, Req = requisicaoJson }, cancellationToken: ct));

        return affected > 0;
    }

    /// <summary>
    /// Conclui a transferência após sucesso externo. Insere em `transferencia` e atualiza `idempotencia.resultado`.
    /// </summary>
    public async Task CompleteAsync(
        Entity.Transferencia entity,
        string idempotencyKey,
        string resultadoJson,
        CancellationToken ct)
    {
        EnsureOpen();

        using var tx = _conn.BeginTransaction();

        const string insertTransferencia = @"
INSERT INTO transferencia
  (idtransferencia, idcontacorrente_origem, idcontacorrente_destino, datamovimento, valor)
VALUES
  (@Id, @Origem, @Destino, @Data, @Valor);";

        var args = new
        {
            Id = entity.IdTransferencia,
            Origem = entity.IdContaCorrenteOrigem,
            Destino = entity.IdContaCorrenteDestino,
            Data = entity.DataMovimento, // "DD/MM/YYYY"
            Valor = entity.Valor
        };

        await _conn.ExecuteAsync(
            new CommandDefinition(insertTransferencia, args, transaction: tx, cancellationToken: ct));

        const string updateIdem = @"
UPDATE idempotencia
   SET resultado = @Res
 WHERE chave_idempotencia = @Key;";

        var upd = await _conn.ExecuteAsync(
            new CommandDefinition(updateIdem, new { Key = idempotencyKey, Res = resultadoJson }, transaction: tx, cancellationToken: ct));

        if (upd == 0)
        {
            tx.Rollback();
            throw new InvalidOperationException("Chave de idempotência não iniciada.");
        }

        tx.Commit();
    }

    /// <summary>
    /// Retorna o JSON de resultado salvo para a chave informada.
    /// </summary>
    public async Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        EnsureOpen();

        const string sql = "SELECT resultado FROM idempotencia WHERE chave_idempotencia = @Key;";
        return await _conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { Key = idempotencyKey }, cancellationToken: ct));
    }

    /// <summary>
    /// Fluxo antigo: grava transferência e idempotência na mesma transação.
    /// Mantém para compatibilidade.
    /// </summary>
    public async Task<bool> TryRegisterAsync(
        Entity.Transferencia entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct)
    {
        EnsureOpen();

        using var tx = _conn.BeginTransaction();

        // Verifica idempotência
        var exists = await _conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM idempotencia WHERE chave_idempotencia = @Key;",
                new { Key = idempotencyKey },
                transaction: tx,
                cancellationToken: ct));

        if (exists > 0)
        {
            tx.Rollback();
            return false;
        }

        // Insere transferência
        const string insertTransferencia = @"
INSERT INTO transferencia
  (idtransferencia, idcontacorrente_origem, idcontacorrente_destino, datamovimento, valor)
VALUES
  (@Id, @Origem, @Destino, @Data, @Valor);";

        var argsTransferencia = new
        {
            Id = entity.IdTransferencia,
            Origem = entity.IdContaCorrenteOrigem,
            Destino = entity.IdContaCorrenteDestino,
            Data = entity.DataMovimento, // "DD/MM/YYYY"
            Valor = entity.Valor
        };

        await _conn.ExecuteAsync(
            new CommandDefinition(insertTransferencia, argsTransferencia, transaction: tx, cancellationToken: ct));

        // Registra idempotência
        const string insertIdem = @"
INSERT INTO idempotencia
  (chave_idempotencia, requisicao, resultado)
VALUES
  (@Key, @Req, @Res);";

        var argsIdem = new { Key = idempotencyKey, Req = requisicaoJson, Res = resultadoJson };

        await _conn.ExecuteAsync(
            new CommandDefinition(insertIdem, argsIdem, transaction: tx, cancellationToken: ct));

        tx.Commit();
        return true;
    }

    public async Task SetErrorResultAsync(string idempotencyKey, string resultadoJson, CancellationToken ct)
    {
        EnsureOpen();

        // Atualizando o resultado do registro iniciado por TryBeginIdempotentAsync
        const string sql = @"
UPDATE idempotencia
   SET resultado = @Res
 WHERE chave_idempotencia = @Key;";

        await _conn.ExecuteAsync(
            new CommandDefinition(sql, new { Key = idempotencyKey, Res = resultadoJson }, cancellationToken: ct));
    }

    private void EnsureOpen()
    {
        if (_conn.State != ConnectionState.Open)
            _conn.Open();
    }
}
