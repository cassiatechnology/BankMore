using System.Security.Claims;
using BankMore.Transferencia.Application.Common;
using BankMore.Transferencia.Application.Transferencias;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace BankMore.Transferencia.Api.Controllers;

// Controller "fino": aplica princípios de CQRS e Single Responsibility.
// Ele faz a ponte HTTP <-> Application (via MediatR), sem conter regra de negócio.
[ApiController]
[Route("api/transferencias")]
public class TransferenciasController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransferenciasController(IMediator mediator) => _mediator = mediator;

    // DTO de transporte (apenas contrato HTTP)
    public sealed record EfetuarTransferenciaRequest(
        string IdempotencyKey,
        string NumeroContaDestino,
        decimal Valor
    );

    [HttpPost]
    [SwaggerOperation(
        Summary = "Efetuar transferência entre contas da mesma instituição",
        Description = "Requer JWT. Validações mínimas (stub). " +
                      "Nos próximos passos: orquestração Débito→Crédito→Compensação e idempotência real.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]                   // Sucesso (stub)
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]  // Erros de regra de negócio
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]   // Token inválido/expirado (mapeado no JwtBearerEvents)
    public async Task<IActionResult> EfetuarTransferencia(
        [FromBody] EfetuarTransferenciaRequest req,
        CancellationToken ct)
    {
        // Recuperamos a identificação da conta de ORIGEM a partir do JWT.
        // Preferência: ClaimTypes.NameIdentifier; fallback: "sub".
        var contaOrigemId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(contaOrigemId))
        {
            // Em condições normais, o filtro global de autorização e o JwtBearerEvents já retornariam 403.
            // Este fallback evita NullReference e esclarece o motivo.
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Token inválido.", type = ErrorCodes.INVALID_ACCOUNT });
        }

        try
        {
            // Aplicando CQRS: Controller cria o Command e delega ao MediatR/Handler.
            var cmd = new EfetuarTransferenciaCommand(
                ContaOrigemId: contaOrigemId,
                NumeroContaDestino: req.NumeroContaDestino,
                Valor: req.Valor,
                IdempotencyKey: req.IdempotencyKey
            );

            await _mediator.Send(cmd, ct);

            // Por contrato, Transferência retorna 204 em caso de sucesso.
            return NoContent();
        }
        catch (TransferenciaException ex)
        {
            // Padrão de erro solicitado: body com { message, type } e HTTP 400
            return BadRequest(new { message = ex.Message, type = ex.ErrorType });
        }
    }
}
