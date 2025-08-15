// Camada: Infrastructure — adapter HTTP para a ContaCorrente.Api.
// Objetivo: enviar comandos de movimentação (C/D) reaproveitando o JWT do usuário.
// Contrato esperado (ContaCorrente.Api /api/conta-corrente/movimentacoes):
//   { idempotencyKey, tipo: 'C'|'D', valor, numeroConta?: int }
//
// Status esperados:
//   204 -> OK
//   400 -> { message, type } (erros de negócio)
//   401/403 -> { message, type } (token inválido/expirado ou sem permissão)

using BankMore.Transferencia.Application.ContaCorrente;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankMore.Transferencia.Infrastructure.Clients;

public sealed class ContaCorrenteClient : IContaCorrenteClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string MovEndpoint = "/api/conta-corrente/movimentacoes";
    private readonly HttpClient _http;

    public ContaCorrenteClient(HttpClient http)
    {
        _http = http;
        // Definindo Accept por padrão
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public Task DebitarAsync(string accessToken, string idempotencyKey, decimal valor, CancellationToken ct)
        => SendMovimentacaoAsync(accessToken, idempotencyKey, 'D', valor, numeroConta: null, ct);

    public Task CreditarAsync(string accessToken, string idempotencyKey, int numeroContaDestino, decimal valor, CancellationToken ct)
        => SendMovimentacaoAsync(accessToken, idempotencyKey, 'C', valor, numeroConta: numeroContaDestino, ct);

    public Task EstornarCreditoOrigemAsync(string accessToken, string idempotencyKey, decimal valor, CancellationToken ct)
        // Estornando com CRÉDITO na conta de origem (tipo 'C', sem numeroConta)
        => SendMovimentacaoAsync(accessToken, idempotencyKey, 'C', valor, numeroConta: null, ct);

    // Enviando POST /movimentacoes com Authorization: Bearer {token}
    private async Task SendMovimentacaoAsync(
        string accessToken,
        string idempotencyKey,
        char tipo,
        decimal valor,
        int? numeroConta,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, MovEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var payload = new MovRequest
        {
            IdempotencyKey = idempotencyKey,
            Tipo = tipo,
            Valor = valor,
            NumeroConta = numeroConta
        };
        req.Content = JsonContent.Create(payload, options: JsonOpts);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if ((int)resp.StatusCode == 204)
            return;

        // Tentando ler { message, type } do body
        string body = string.Empty;
        string errorType = "UNKNOWN";
        string message = $"Erro HTTP {(int)resp.StatusCode} ao chamar ContaCorrente.";

        try
        {
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var err = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOpts);
                if (err is not null)
                {
                    if (!string.IsNullOrWhiteSpace(err.Type)) errorType = err.Type!;
                    if (!string.IsNullOrWhiteSpace(err.Message)) message = err.Message!;
                }
            }
        }
        catch
        {
            // Ignorando falha de parsing; usando mensagem padrão
        }

        throw new ContaCorrenteClientException(message, errorType, (int)resp.StatusCode);
    }

    private sealed class MovRequest
    {
        public string IdempotencyKey { get; set; } = default!;
        public char Tipo { get; set; }
        public decimal Valor { get; set; }
        public int? NumeroConta { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
        public string? Type { get; set; }
    }
}
