// Camada: Infrastructure — implementação Dapper de IMovimentoRepository.
// Aplicando Ports & Adapters: a Application conhece a interface; aqui faço o acesso ao SQLite.
// Mantendo transação na inserção idempotente e cálculos simples para saldo.

using BankMore.ContaCorrente.Application.Movimentacao;
using BankMore.ContaCorrente.Domain.Entities;
using Dapper;
using System.Data;

namespace BankMore.ContaCorrente.Infrastructure.Repositories;

public sealed class MovimentoRepository : IMovimentoRepository
{
    private readonly IDbConnection _conn;

    public MovimentoRepository(IDbConnection conn) => _conn = conn;

    public async Task<bool> TryRegistrarAsync(
        Movimento entity,
        string idempotencyKey,
        string requisicaoJson,
        string resultadoJson,
        CancellationToken ct)
    {
        EnsureOpen();

        using var tx = _conn.BeginTransaction();

        // Checando idempotência: se a chave já existe, não repito o efeito
        var exists = await _conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM idempotencia WHERE chave_idempotencia = @key;",
                new { key = idempotencyKey },
                transaction: tx,
                cancellationToken: ct));

        if (exists > 0)
        {
            tx.Rollback();
            return false;
        }

        // Inserindo o movimento (C/D)
        const string insertMov = @"
INSERT INTO movimento
  (idmovimento, idcontacorrente, datamovimento, tipomovimento, valor)
VALUES
  (@Id, @Conta, @Data, @Tipo, @Valor);";

        var argsMov = new
        {
            Id = entity.IdMovimento,
            Conta = entity.IdContaCorrente,
            Data = entity.DataMovimento,     // "DD/MM/YYYY"
            Tipo = entity.TipoMovimento,     // 'C' ou 'D'
            Valor = entity.Valor              // decimal -> REAL no SQLite
        };

        await _conn.ExecuteAsync(
            new CommandDefinition(
                insertMov, argsMov, transaction: tx, cancellationToken: ct));

        // Registrando idempotência (auditoria/replay)
        const string insertIdem = @"
INSERT INTO idempotencia
  (chave_idempotencia, requisicao, resultado)
VALUES
  (@Key, @Req, @Res);";

        await _conn.ExecuteAsync(
            new CommandDefinition(
                insertIdem,
                new { Key = idempotencyKey, Req = requisicaoJson, Res = resultadoJson },
                transaction: tx,
                cancellationToken: ct));

        tx.Commit();
        return true;
    }

    public async Task<decimal> ObterSaldoAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        // Somando créditos e débitos e retornando diferença
        const string sql = @"
SELECT
  COALESCE(SUM(CASE WHEN tipomovimento = 'C' THEN valor ELSE 0 END), 0)
- COALESCE(SUM(CASE WHEN tipomovimento = 'D' THEN valor ELSE 0 END), 0)
FROM movimento
WHERE idcontacorrente = @Id;";

        var saldo = await _conn.ExecuteScalarAsync<double>(
            new CommandDefinition(sql, new { Id = contaId }, cancellationToken: ct));

        // Convertendo para decimal para manter consistência na Application
        return Convert.ToDecimal(saldo);
    }

    public async Task<bool> ContaExisteAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        var count = await _conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM contacorrente WHERE idcontacorrente = @Id;",
                new { Id = contaId },
                cancellationToken: ct));

        return count > 0;
    }

    public async Task<bool> ContaAtivaAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        var ativo = await _conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                "SELECT ativo FROM contacorrente WHERE idcontacorrente = @Id;",
                new { Id = contaId },
                cancellationToken: ct));

        // Retornando false quando a conta não existe
        return ativo.HasValue && ativo.Value == 1;
    }

    public async Task<string?> GetContaIdByNumeroAsync(int numero, CancellationToken ct)
    {
        EnsureOpen();

        const string sql = @"
SELECT idcontacorrente
FROM contacorrente
WHERE numero = @Numero
LIMIT 1;";

        return await _conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { Numero = numero }, cancellationToken: ct));
    }

    public async Task<string?> GetResultadoByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        EnsureOpen();

        return await _conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                "SELECT resultado FROM idempotencia WHERE chave_idempotencia = @key;",
                new { key = idempotencyKey },
                cancellationToken: ct));
    }

    public async Task<(int Numero, string Nome)?> GetContaCabecalhoAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        // Mapeando para uma classe com setters para evitar problemas de Int64 -> int
        const string sql = @"
SELECT
  numero AS Numero,
  nome   AS Nome
FROM contacorrente
WHERE idcontacorrente = @Id
LIMIT 1;";

        var row = await _conn.QuerySingleOrDefaultAsync<ContaCab>(
            new CommandDefinition(sql, new { Id = contaId }, cancellationToken: ct));

        if (row is null) return null;
        return (row.Numero, row.Nome);
    }

    // Classe auxiliar de mapeamento (somente para este repositório)
    private sealed class ContaCab
    {
        public int Numero { get; set; }
        public string Nome { get; set; } = default!;
    }

    private void EnsureOpen()
    {
        if (_conn.State != ConnectionState.Open)
            _conn.Open();
    }
}
