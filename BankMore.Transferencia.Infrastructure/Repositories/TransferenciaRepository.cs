// Camada: Infrastructure (DDD) — implementação Dapper da porta ITransferenciaRepository.
// Conceitos aplicados:
// - "Ports & Adapters": a Application depende da interface; aqui adaptamos para SQLite/Dapper.
// - Idempotência: checamos a chave em 'idempotencia'; se existir, NADA é re-executado.
// - Transação: inserimos 'transferencia' e 'idempotencia' ATOMICAMENTE (uma única transação).
//
// Observações:
// - Tipos do schema seguem o SQL do desafio (TEXT/REAL). O domínio usa decimal; Dapper/SQLite convertem.
// - DataMovimento é "DD/MM/YYYY" (conforme script). Consultas e formatações serão tratadas na Application.

using System.Data;
using Dapper;
using BankMore.Transferencia.Application.Transferencias;
using TransferenciaDomain = BankMore.Transferencia.Domain.Entities;

namespace BankMore.Transferencia.Infrastructure.Repositories;

public sealed class TransferenciaRepository : ITransferenciaRepository
{
    private readonly IDbConnection _conn;

    public TransferenciaRepository(IDbConnection conn)
        => _conn = conn;

    public async Task<bool> TryRegisterAsync(
        TransferenciaDomain.Transferencia entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct)
    {
        // Iniciamos uma transação para garantir atomicidade entre as duas tabelas.
        using var tx = _conn.BeginTransaction();

        // 1) Checagem de idempotência: se já processamos essa chave, não reexecutamos.
        var exists = await _conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM idempotencia WHERE chave_idempotencia = @key;",
            new { key = idempotencyKey }, tx);

        if (exists > 0)
        {
            tx.Rollback(); // nada a fazer; evitamos efeitos colaterais
            return false;  // sinaliza ao Handler que é um replay
        }

        // 2) Inserir a transferência (schema do arquivo transferencia.sql)
        const string insertTransferencia = @"
INSERT INTO transferencia
    (idtransferencia, idcontacorrente_origem, idcontacorrente_destino, datamovimento, valor)
VALUES
    (@Id, @Origem, @Destino, @DataMov, @Valor);";

        var argsTransferencia = new
        {
            Id = entity.IdTransferencia,
            Origem = entity.IdContaCorrenteOrigem,
            Destino = entity.IdContaCorrenteDestino,
            DataMov = entity.DataMovimento, // "DD/MM/YYYY"
            Valor = entity.Valor          // decimal -> o provedor cuida da conversão p/ REAL
        };

        await _conn.ExecuteAsync(insertTransferencia, argsTransferencia, tx);

        // 3) Registrar a idempotência (request/response para auditoria e replay)
        const string insertIdempotencia = @"
INSERT INTO idempotencia
    (chave_idempotencia, requisicao, resultado)
VALUES
    (@Key, @Req, @Res);";

        var argsIdempotencia = new
        {
            Key = idempotencyKey,
            Req = requisicaoJson,
            Res = resultadoJson
        };

        await _conn.ExecuteAsync(insertIdempotencia, argsIdempotencia, tx);

        // 4) Commit final — garante "tudo ou nada"
        tx.Commit();
        return true; // primeira execução (persistiu)
    }

    public async Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        // Recupera o resultado previamente armazenado para a chave (útil no replay)
        const string sql = "SELECT resultado FROM idempotencia WHERE chave_idempotencia = @key;";
        return await _conn.QuerySingleOrDefaultAsync<string?>(sql, new { key = idempotencyKey });
    }
}
