using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BankMore.Transferencia.Application.Common;            // ErrorCodes
using BankMore.Transferencia.Application.Transferencias;   // EfetuarTransferenciaCommand, TransferenciaException
using BankMore.Transferencia.Application.ContaCorrente;    // ContaCorrenteClientException

namespace BankMore.Transferencia.Api.Controllers;

[ApiController]
[Route("api/transferencias")]
[Authorize] // protegendo o endpoint com JWT
public sealed class TransferenciasController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransferenciasController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Efetuando transferência entre contas da mesma instituição.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Efetuar([FromBody] EfetuarTransferenciaRequest req, CancellationToken ct)
    {
        // Lendo id da conta do JWT
        var contaOrigemId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(contaOrigemId))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Token inválido.", type = ErrorCodes.INVALID_ACCOUNT });

        // Extraindo o token do header Authorization
        var authHeader = Request.Headers["Authorization"].ToString();
        string accessToken = string.Empty;
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            const string bearer = "Bearer ";
            accessToken = authHeader.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
                ? authHeader.Substring(bearer.Length).Trim()
                : authHeader.Trim();
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Token ausente.", type = ErrorCodes.INVALID_ACCOUNT });

        try
        {
            // Montando o command com dados do body, id da conta do token e o JWT
            var cmd = new EfetuarTransferenciaCommand(
                ContaOrigemId: contaOrigemId,
                NumeroContaDestino: req.NumeroContaDestino,
                Valor: req.Valor,
                IdempotencyKey: req.IdempotencyKey,
                AccessToken: accessToken
            );

            await _mediator.Send(cmd, ct);

            // Retornando 204 quando concluir
            return NoContent();
        }
        catch (ContaCorrenteClientException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            // Token inválido/expirado → 403
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message, type = ex.ErrorType });
        }
        catch (TransferenciaException ex)
        {
            // Mapeando falhas de regra para 400
            return BadRequest(new { message = ex.Message, type = ex.ErrorType });
        }
    }

    // DTO do body
    public sealed record EfetuarTransferenciaRequest(
        string IdempotencyKey,     // chave idempotente do cliente
        string NumeroContaDestino, // número da conta de destino
        decimal Valor              // valor da transferência
    );
}
