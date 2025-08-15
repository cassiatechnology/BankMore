using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BankMore.Transferencia.Infrastructure.Db;

/// <summary>
/// Camada INFRA (DDD): bootstrap de banco.
/// Lê o script SQL "oficial" (Embedded Resource) e executa no SQLite.
/// Vantagem do Embedded Resource: portável (funciona igual em Docker) e versionado junto com o código.
/// </summary>
public static class DbInitializer
{
    // IMPORTANTE: o nome do recurso precisa bater com o DefaultNamespace + caminho do arquivo.
    // Conferir com: typeof(DbInitializer).Assembly.GetManifestResourceNames()
    private const string ResourceName =
        "BankMore.Transferencia.Infrastructure.Db.Scripts.transferencia.sql";

    /// <summary>
    /// Garante diretório do .db, habilita FK e executa o script de criação de schema.
    /// </summary>
    public static void EnsureCreated(string connectionString, Action<string>? log = null)
    {
        // 1) Garante que a pasta do arquivo .db exista (ex.: Data Source=./data/transferencia.db)
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dbPath = Path.GetFullPath(builder.DataSource);
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            log?.Invoke($"[DbInit] Diretorio criado: {dir}");
        }

        // 2) Carrega o SQL do recurso embutido
        var asm = typeof(DbInitializer).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            // Dica de diagnóstico: liste os nomes disponíveis
            var names = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded SQL resource nao encontrado: '{ResourceName}'. Disponiveis: {names}");
        }
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        // 3) Abre conexão e executa o script dentro de uma transação
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

        // Observações:
        // - O script tem múltiplas instruções; o provider do SQLite aceita em um único CommandText.
        // - Mantivemos tipos/formatos fiéis ao arquivo (TEXT/REAL; data como TEXT DD/MM/YYYY).
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        tx.Commit();
        log?.Invoke("[DbInit] transferencia.sql executado com sucesso.");
    }
}
