// Camada: Infrastructure (Ports & Adapters)
// Responsabilidade: implementar a "porta" IPasswordHasher com PBKDF2 (RFC2898).
// Decisões de segurança:
// - Salt aleatório (16 bytes) por senha, armazenado em Base64 (coluna SALT).
// - Iterações: 100.000 (DEV). Em produção, considere ≥210.000 (depende de budget de CPU).
// - Tamanho da chave (DK): 32 bytes (256 bits) em Base64 (coluna SENHA).
// - Comparação em tempo constante (FixedTimeEquals) para evitar timing attacks.
// Observação: o desafio pede apenas SENHA (hash) e SALT; não vamos versionar algoritmo/iters no banco.
//            Caso precise rotacionar parâmetros, uma coluna "algo" ou "rev" ajudaria.

using System.Security.Cryptography;
using System.Text;
using BankMore.ContaCorrente.Application.Auth;

namespace BankMore.ContaCorrente.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    // Parâmetros PBKDF2 (ajustáveis conforme ambiente)
    private const int DefaultSaltSizeBytes = 16;     // 128 bits
    private const int KeySizeBytes = 32;             // 256 bits
    private const int Iterations = 100_000;          // DEV: 100k. Prod: calibre para sua infra.

    public string GenerateSalt(int sizeBytes = DefaultSaltSizeBytes)
    {
        // Gera salt criptograficamente seguro
        byte[] salt = RandomNumberGenerator.GetBytes(sizeBytes);
        return Convert.ToBase64String(salt);
    }

    public string Hash(string password, string saltBase64)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Senha não pode ser vazia.", nameof(password));
        if (string.IsNullOrEmpty(saltBase64))
            throw new ArgumentException("Salt não pode ser vazio.", nameof(saltBase64));

        byte[] salt = Convert.FromBase64String(saltBase64);
        byte[] pwd = Encoding.UTF8.GetBytes(password);

        // PBKDF2 (HMACSHA256). Alternativas: SHA512 ou Rfc2898DeriveBytes com PRF configurável.
        byte[] dk = Rfc2898DeriveBytes.Pbkdf2(
            password: pwd,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySizeBytes);

        return Convert.ToBase64String(dk);
    }

    public bool Verify(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(hashBase64)) return false;

        string recomputed = Hash(password, saltBase64);

        // Comparação em tempo constante
        byte[] a = Convert.FromBase64String(hashBase64);
        byte[] b = Convert.FromBase64String(recomputed);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
