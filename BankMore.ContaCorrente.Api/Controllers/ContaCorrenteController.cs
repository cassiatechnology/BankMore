using System.Security.Claims;
using BankMore.ContaCorrente.Application.Auth;
using BankMore.ContaCorrente.Application.Cadastro;
using BankMore.ContaCorrente.Application.Common;
using BankMore.ContaCorrente.Application.Inativacao;
using BankMore.ContaCorrente.Application.Movimentacao;
using BankMore.ContaCorrente.Application.Saldo;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

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
    public sealed record InativarContaRequest(string Senha);

    [HttpPost("inativar")]
    [SwaggerOperation(Summary = "Inativar conta corrente", Description = "Requer JWT válido e senha da conta")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Inativar([FromBody] InativarContaRequest req, CancellationToken ct)
    {
        // Pegamos o "sub" do JWT como id da conta (foi assim que geramos no TokenService)
        var contaId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub") // fallback
                      ?? "1"; // stub de segurança para rodar local

        try
        {
            await _mediator.Send(new InativarContaCommand(contaId, req.Senha), ct);
            return NoContent();
        }
        catch (InativarContaException ex)
        {
            // Poderíamos mapear outros códigos; aqui voltamos 400 com tipo genérico
            return BadRequest(new { message = ex.Message, type = ErrorCodes.USER_UNAUTHORIZED });
        }
    }

    // -------- MOVIMENTAÇÃO --------
    public sealed record MovimentarRequest(string IdempotencyKey, string? NumeroConta, decimal Valor, char Tipo);

    [HttpPost("movimentacoes")]
    [SwaggerOperation(Summary = "Movimentar conta corrente", Description = "Crédito ou Débito; idempotente por IdempotencyKey")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Movimentar([FromBody] MovimentarRequest req, CancellationToken ct)
    {
        var contaIdFromToken = User.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? User.FindFirstValue("sub")
                               ?? "1"; // stub

        try
        {
            // Regra: se NumeroConta não informado, usar a conta do token (merge de transporte → domínio)
            var cmd = new MovimentarContaCommand(
                IdempotencyKey: req.IdempotencyKey,
                NumeroConta: req.NumeroConta ?? contaIdFromToken,
                Valor: req.Valor,
                Tipo: req.Tipo
            );

            await _mediator.Send(cmd, ct);
            return NoContent();
        }
        catch (MovimentacaoException ex)
        {
            return BadRequest(new { message = ex.Message, type = ErrorCodes.INVALID_VALUE });
        }
    }

    // -------- SALDO --------
    [HttpGet("saldo")]
    [SwaggerOperation(Summary = "Consultar saldo", Description = "Requer JWT válido")]
    [ProducesResponseType(typeof(SaldoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Saldo(CancellationToken ct)
    {
        var contaId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub")
                      ?? "1"; // stub
        var dto = await _mediator.Send(new ConsultarSaldoQuery(contaId), ct);
        return Ok(dto);
    }
}
