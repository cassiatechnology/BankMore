// Camada: Application (DDD - Port).
// Responsabilidade: definir a interface de PERSISTÊNCIA para "Conta".
// A Infrastructure (Dapper/SQLite) fornecerá a implementação concreta.
// Por que assim? Mantemos a Application desacoplada de detalhes de banco/ORM (Ports & Adapters).

using System.Threading;
using System.Threading.Tasks;

namespace BankMore.ContaCorrente.Application.Contas;

public interface IContaRepository
{
    /// <summary>
    /// Cria uma conta corrente com CPF único, gera um id (GUID) e aloca um NÚMERO de conta único.
    /// Persiste hash e salt da senha.
    /// </summary>
    /// <param name="cpf11">CPF já normalizado (11 dígitos)</param>
    /// <param name="nome">Nome do titular</param>
    /// <param name="senhaHashBase64">Hash PBKDF2 (Base64)</param>
    /// <param name="saltBase64">Salt usado no hash (Base64)</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>(IdConta, Numero) gerados e persistidos</returns>
    Task<(string IdConta, int Numero)> CreateAsync(
        string cpf11,
        string nome,
        string senhaHashBase64,
        string saltBase64,
        CancellationToken ct);

    /// <summary>
    /// Retorna a conta pelo CPF (11 dígitos) OU pelo número da conta.
    /// Usado no login para localizar a conta e validar senha/hash.
    /// </summary>
    /// <param name="cpfOuNumero">Entrada do usuário (pode ser "12345678901" ou "12345")</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>Modelo de leitura da conta, ou null se não encontrada</returns>
    Task<ContaReadModel?> GetByCpfOrNumeroAsync(string cpfOuNumero, CancellationToken ct);
}
