// Camada: Infrastructure (DDD - Adapters)
// Responsabilidade: implementar a porta IContaRepository com Dapper/SQLite.
// Conceitos aplicados:
// - Ports & Adapters: Application depende da interface; Infra fornece a implementação.
// - Transação: criação de conta é atômica (gera id, aloca número único, insere).
// - Resiliência simples: retry em colisão de número (UNIQUE constraint).
//
// Observações de design:
// - Criamos a conta já ATIVA (ativo = 1). O enunciado descreve "Inativar conta", mas não
//   existe um passo de "ativação"; portanto, ativamos na criação para permitir uso imediato.
// - Número de conta: estratégia simples "MAX(numero)+1". Em produção (Oracle), usaríamos SEQUENCE.

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using BankMore.ContaCorrente.Application.Contas;

namespace BankMore.ContaCorrente.Infrastructure.Repositories;

public sealed class ContaRepository : IContaRepository
{
    private readonly IDbConnection _conn;

    public ContaRepository(IDbConnection conn) => _conn = conn;

    public async Task<(string IdConta, int Numero)> CreateAsync(
        string cpf11,
        string nome,
        string senhaHashBase64,
        string saltBase64,
        CancellationToken ct)
    {
        EnsureOpen();

        // 1) Inicia transação para atomicidade
        using var tx = _conn.BeginTransaction();

        // 2) Gera id (GUID em string) e calcula número único
        string id = Guid.NewGuid().ToString();

        // Estratégia simples: MAX(numero) + 1. Em produção, usar SEQUENCE.
        int nextNumber = await _conn.ExecuteScalarAsync<int?>(
            "SELECT MAX(numero) FROM contacorrente;", transaction: tx) is int max ? max + 1 : 100000;

        // 3) Tenta inserir; se colidir (UNIQUE), incrementa e tenta novamente (poucas vezes)
        const string insertSql = @"
INSERT INTO contacorrente
    (idcontacorrente, numero, cpf, nome, ativo, senha, salt)
VALUES
    (@Id, @Numero, @Cpf, @Nome, @Ativo, @Senha, @Salt);";

        int tries = 0;
        while (true)
        {
            try
            {
                var args = new
                {
                    Id = id,
                    Numero = nextNumber,
                    Cpf = cpf11,
                    Nome = string.IsNullOrWhiteSpace(nome) ? "Titular" : nome.Trim(),
                    Ativo = 1, // conta nasce ativa
                    Senha = senhaHashBase64,
                    Salt = saltBase64
                };

                await _conn.ExecuteAsync(insertSql, args, tx);
                tx.Commit();
                return (id, nextNumber);
            }
            catch (SqliteException ex) when (IsUniqueConstraint(ex))
            {
                // Colisão de UNIQUE (numero ou cpf). Vamos identificar e agir:
                // - Se for CPF duplicado, não adianta retry (vamos relançar).
                // - Se for número duplicado, incrementamos e tentamos de novo (poucas vezes).
                if (ex.Message.Contains("contacorrente.cpf", StringComparison.OrdinalIgnoreCase))
                {
                    tx.Rollback();
                    throw new InvalidOperationException("CPF já cadastrado.");
                }

                if (ex.Message.Contains("contacorrente.numero", StringComparison.OrdinalIgnoreCase))
                {
                    tries++;
                    if (tries > 5)
                    {
                        tx.Rollback();
                        throw new InvalidOperationException("Falha ao alocar número de conta (muitas colisões).");
                    }
                    nextNumber++; // tenta próximo número
                    continue;
                }

                // Outra violação de UNIQUE não esperada
                tx.Rollback();
                throw;
            }
        }
    }

    public async Task<ContaReadModel?> GetByCpfOrNumeroAsync(string cpfOuNumero, CancellationToken ct)
    {
        EnsureOpen();

        // Aceita CPF (11 dígitos) ou número (int).
        // Para simplicidade, consulta ambos os campos com OR.
        int numeroParsed = -1;
        _ = int.TryParse(cpfOuNumero, out numeroParsed);
        string cpfNormalized = OnlyDigits(cpfOuNumero);

        const string sql = @"
SELECT
    idcontacorrente   AS IdConta,
    numero            AS Numero,
    cpf               AS Cpf,
    nome              AS Nome,
    CASE WHEN ativo = 1 THEN 1 ELSE 0 END AS Ativo,
    senha             AS SenhaHash,
    salt              AS Salt
FROM contacorrente
WHERE cpf = @Cpf OR numero = @Numero
LIMIT 1;";

        var result = await _conn.QuerySingleOrDefaultAsync<ContaReadModel>(
            sql,
            new { Cpf = cpfNormalized, Numero = numeroParsed });

        return result;
    }

    public async Task<(string SenhaHash, string Salt, bool Ativo)?> GetAuthInfoByIdAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        const string sql = @"
SELECT
    senha AS SenhaHash,
    salt  AS Salt,
    CASE WHEN ativo = 1 THEN 1 ELSE 0 END AS Ativo
FROM contacorrente
WHERE idcontacorrente = @Id
LIMIT 1;";

        var row = await _conn.QuerySingleOrDefaultAsync<AuthRow>(
            new CommandDefinition(sql, new { Id = contaId }, cancellationToken: ct));

        if (row is null) return null;
        return (row.SenhaHash, row.Salt, row.Ativo == 1);
    }

    public async Task InativarAsync(string contaId, CancellationToken ct)
    {
        EnsureOpen();

        const string sql = @"UPDATE contacorrente SET ativo = 0 WHERE idcontacorrente = @Id AND ativo = 1;";
        await _conn.ExecuteAsync(
            new CommandDefinition(sql, new { Id = contaId }, cancellationToken: ct));
    }

    private static bool IsUniqueConstraint(SqliteException ex)
        => ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */;

    private static string OnlyDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var arr = new char[s.Length];
        var j = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') arr[j++] = c;
        }
        return new string(arr, 0, j);
    }

    private sealed class AuthRow
    {
        public string SenhaHash { get; set; } = default!;
        public string Salt { get; set; } = default!;
        public int Ativo { get; set; }
    }

    /// <summary>
    /// Usando EnsureOpen para garantir conexão aberta antes do comando.
    /// </summary>
    private void EnsureOpen()
    {
        if (_conn.State != ConnectionState.Open)
            _conn.Open();
    }

}
