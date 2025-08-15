// Camada: Application
// Responsabilidade: definir a "porta" para hashing de senhas.
// Motivo: a Application não deve depender de uma implementação específica (PBKDF2, Argon2, etc.).
// A Infrastructure fornecerá a implementação concreta e o Program.cs fará a injeção (DI).

namespace BankMore.ContaCorrente.Application.Auth;

public interface IPasswordHasher
{
    /// <summary>
    /// Gera um salt aleatório e retorna em Base64.
    /// Tamanho padrão (16 bytes) é suficiente para PBKDF2 nesse cenário.
    /// </summary>
    string GenerateSalt(int sizeBytes = 16);

    /// <summary>
    /// Gera o hash (Base64) da senha, usando o salt (Base64).
    /// Observação: manteremos padrão consistente (Base64) para persistir em TEXT no SQLite.
    /// </summary>
    string Hash(string password, string saltBase64);

    /// <summary>
    /// Verifica se o <paramref name="password"/> corresponde ao <paramref name="hashBase64"/>
    /// quando combinado com o <paramref name="saltBase64"/>.
    /// </summary>
    bool Verify(string password, string saltBase64, string hashBase64);
}
