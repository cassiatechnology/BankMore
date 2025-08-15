using Microsoft.Data.Sqlite;

namespace BankMore.ContaCorrente.Infrastructure.Db;

/// <summary>
/// Camada INFRA (DDD): bootstrap de banco para o microsserviço de Conta Corrente.
/// Lê o script SQL (Embedded Resource) e executa no SQLite.
/// </summary>
public static class DbInitializer
{
    // Nome do recurso embutido (DefaultNamespace do projeto + caminho do arquivo)
    // Dica: se houver dúvida, liste com: typeof(DbInitializer).Assembly.GetManifestResourceNames()
    private const string ResourceName =
        "BankMore.ContaCorrente.Infrastructure.Db.Scripts.contacorrente.sql";

    /// <summary>
    /// Garante diretório do .db, habilita FK e executa o script de criação do schema.
    /// </summary>
    public static void EnsureCreated(string connectionString, Action<string>? log = null)
    {
        // 1) Garantir a pasta do arquivo .db (ex.: Data Source=./data/contacorrente.db)
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dbPath = Path.GetFullPath(builder.DataSource);
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            log?.Invoke($"[ContaDbInit] Diretorio criado: {dir}");
        }

        // 2) Carregar o SQL do recurso embutido
        var asm = typeof(DbInitializer).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            var names = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded SQL resource nao encontrado: '{ResourceName}'. Disponivel: {names}");
        }
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        // 3) Abrir conexão e executar dentro de uma transação
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        // Habilita validação de FKs no SQLite
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        // Observação: o script contém múltiplos CREATE TABLE IF NOT EXISTS; pode ser executado em um único command.
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        tx.Commit();
        log?.Invoke("[ContaDbInit] contacorrente.sql executado com sucesso.");
    }
}
