using System.Security.Claims;
using BankMore.ContaCorrente.Application.Auth;
using BankMore.ContaCorrente.Application.Cadastro;
using BankMore.ContaCorrente.Application.Common;
using BankMore.ContaCorrente.Application.Movimentacao;
using BankMore.ContaCorrente.Application.Saldo;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using BankMore.ContaCorrente.Application.Contas;   // InativarContaCommand, InativacaoException

namespace BankMore.ContaCorrente.Api.Controllers;

// Controller "fino": não contém regra de negócio.
// Apenas converte HTTP <-> MediatR (CQRS) e aplica Auth/validações específicas de transporte.
[ApiController]
[Route("api/conta-corrente")]
public class ContaCorrenteController : ControllerBase
{
    private readonly IMediator _mediator;

    public ContaCorrenteController(IMediator mediator) => _mediator = mediator;

    // -------- CADASTRO --------
    public sealed record CadastrarContaRequest(string Cpf, string Senha);

    [AllowAnonymous] // Cadastro não exige token
    [HttpPost("cadastro")]
    [SwaggerOperation(Summary = "Cadastrar conta corrente", Description = "Recebe CPF e senha; retorna número da conta")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarContaRequest req, CancellationToken ct)
    {
        try
        {
            var numeroConta = await _mediator.Send(new CadastrarContaCommand(req.Cpf, req.Senha), ct);
            return Ok(numeroConta);
        }
        catch (CadastrarContaException ex)
        {
            return BadRequest(new { message = ex.Message, type = ErrorCodes.INVALID_DOCUMENT });
        }
    }

    // -------- LOGIN --------
    public sealed record LoginRequest(string CpfOuConta, string Senha);

    [AllowAnonymous] // Login não exige token
    [HttpPost("login")]
    [SwaggerOperation(Summary = "Efetuar login", Description = "Recebe CPF ou Número da Conta e a senha; retorna um JWT")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        try
        {
            var jwt = await _mediator.Send(new EfetuarLoginCommand(req.CpfOuConta, req.Senha), ct);
            return Ok(jwt);
        }
        catch (LoginException ex)
        {
            // Requisito: 401 USER_UNAUTHORIZED no login inválido
            return StatusCode(StatusCodes.Status401Unauthorized, new { message = ex.Message, type = ErrorCodes.USER_UNAUTHORIZED });
        }
    }

    // -------- INATIVAR CONTA --------
    public sealed record InativarRequest(string Senha);

    [HttpPost("inativar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Inativar([FromBody] InativarRequest req, CancellationToken ct)
    {
        // Lendo id da conta a partir do JWT
        var contaId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(contaId))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Token inválido.", type = ErrorCodes.INVALID_ACCOUNT });

        try
        {
            // Enviando o command
            await _mediator.Send(new InativarContaCommand(contaId, req.Senha), ct);
            return NoContent(); // 204
        }
        catch (InativacaoException ex) when (ex.ErrorType == ErrorCodes.USER_UNAUTHORIZED)
        {
            // Senha incorreta → 401
            return StatusCode(StatusCodes.Status401Unauthorized, new { message = ex.Message, type = ex.ErrorType });
        }
        catch (InativacaoException ex)
        {
            // Conta inexistente ou inválida → 400
            return BadRequest(new { message = ex.Message, type = ex.ErrorType });
        }
    }


    // -------- MOVIMENTAÇÃO --------
    public sealed record MovimentarRequest(
    string IdempotencyKey,
    char Tipo,           // 'C' ou 'D'
    decimal Valor,
    int? NumeroConta     // opcional; se null, usar a conta do token
    );

    [HttpPost("movimentacoes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Movimentar([FromBody] MovimentarRequest req, CancellationToken ct)
    {
        // Lendo id da conta a partir do JWT
        var contaTokenId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(contaTokenId))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Token inválido.", type = ErrorCodes.INVALID_ACCOUNT });

        try
        {
            // Montando o Command com a nova assinatura
            var cmd = new MovimentarContaCommand(
                ContaTokenId: contaTokenId,
                IdempotencyKey: req.IdempotencyKey,
                Tipo: req.Tipo,
                Valor: req.Valor,
                NumeroConta: req.NumeroConta
            );

            await _mediator.Send(cmd, ct);
            return NoContent(); // 204
        }
        catch (MovimentacaoException ex)
        {
            // Mapeando falhas de regra para 400
            return BadRequest(new { message = ex.Message, type = ex.ErrorType });
        }
    }


    // -------- SALDO --------
    [HttpGet("saldo")]
    [ProducesResponseType(typeof(SaldoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterSaldo(CancellationToken ct)
    {
        // Lendo id da conta a partir do JWT
        var contaId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(contaId))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Token inválido.", type = ErrorCodes.INVALID_ACCOUNT });

        try
        {
            // Enviando a query para obter o saldo
            var dto = await _mediator.Send(new ConsultarSaldoQuery(contaId), ct);

            // Retornando 200 com o DTO de saldo (DataHora já no fuso America/Sao_Paulo)
            return Ok(dto);
        }
        catch (SaldoException ex)
        {
            // Mapeando falhas de regra para 400
            return BadRequest(new { message = ex.Message, type = ex.ErrorType });
        }
    }

}
