using BankMore.ContaCorrente.Application.Auth;
using BankMore.ContaCorrente.Application.Cadastro;
using BankMore.ContaCorrente.Application.Common;
using BankMore.ContaCorrente.Application.Contas;   // InativarContaCommand, InativacaoException
using BankMore.ContaCorrente.Application.Movimentacao;
using BankMore.ContaCorrente.Application.Saldo;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

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
    /// <summary>Dados para cadastro de conta.</summary>
    /// <remarks>Recebendo CPF e senha.</remarks>
    public sealed record CadastrarContaRequest(string Cpf, string Senha);

    /// <summary>Cadastrando conta corrente.</summary>
    /// <remarks>
    /// Recebendo CPF e senha, validando o documento e retornando o número da conta.
    /// 
    /// <b>Exemplo de request</b>
    /// { "cpf": "529.982.247-25", "senha": "123456" }
    ///
    /// <b>200 OK</b>
    /// { "numeroConta": 100000 }
    ///
    /// <b>400 BadRequest</b>
    /// { "message": "CPF inválido.", "type": "INVALID_DOCUMENT" }
    /// </remarks>
    /// <param name="req">CPF e senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="200">Retornando o número da conta.</response>
    /// <response code="400">CPF inválido (<c>INVALID_DOCUMENT</c>).</response>
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
    /// <summary>Dados para login.</summary>
    /// <remarks>Recebendo CPF <i>ou</i> Número da Conta e a senha.</remarks>
    public sealed record LoginRequest(string CpfOuConta, string Senha);

    /// <summary>Efetuando login.</summary>
    /// <remarks>
    /// Recebendo número da conta <i>ou</i> CPF e a senha. Retornando um token JWT.
    /// 
    /// <b>Exemplo (por CPF)</b>
    /// { "cpfOuConta": "52998224725", "senha": "123456" }
    ///
    /// <b>Exemplo (por número)</b>
    /// { "cpfOuConta": "100000", "senha": "123456" }
    ///
    /// <b>200 OK</b>
    /// { "token": "eyJhbGciOi..." }
    ///
    /// <b>401 Unauthorized</b>
    /// { "message": "Credenciais inválidas.", "type": "USER_UNAUTHORIZED" }
    /// </remarks>
    /// <param name="req">Documento ou número + senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="200">Retornando o token JWT.</response>
    /// <response code="401">Credenciais inválidas (<c>USER_UNAUTHORIZED</c>).</response>
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
    /// <summary>Dados para inativação.</summary>
    /// <remarks>Recebendo a senha do titular para confirmar a operação.</remarks>
    public sealed record InativarRequest(string Senha);

    /// <summary>Inativando a conta corrente.</summary>
    /// <remarks>
    /// Validando a senha do titular e marcando <c>ativo = 0</c>.
    /// 
    /// <b>Exemplo de request</b>
    /// { "senha": "123456" }
    ///
    /// <b>204 No Content</b> — operação concluída
    ///
    /// <b>401 Unauthorized</b>
    /// { "message": "Senha incorreta.", "type": "USER_UNAUTHORIZED" }
    ///
    /// <b>400 BadRequest</b>
    /// { "message": "Conta não encontrada.", "type": "INVALID_ACCOUNT" }
    ///
    /// <b>403 Forbidden</b>
    /// { "message": "Token inválido.", "type": "INVALID_ACCOUNT" }
    /// </remarks>
    /// <param name="req">Senha do titular.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="204">Conta inativada.</response>
    /// <response code="400">Conta inexistente/ inválida (<c>INVALID_ACCOUNT</c>).</response>
    /// <response code="401">Senha incorreta (<c>USER_UNAUTHORIZED</c>).</response>
    /// <response code="403">Token inválido/expirado.</response>
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
    /// <summary>Dados para movimentação (crédito/débito).</summary>
    /// <remarks>
    /// Usando a conta do token quando <c>NumeroConta</c> não é informado.
    /// Regras:
    /// - Aceitando <c>Tipo</c>: 'C' (Crédito) ou 'D' (Débito).
    /// - Apenas valores positivos.
    /// - Quando <c>NumeroConta</c> é diferente da conta do token, aceitando apenas 'C'.
    /// - Idempotência via <c>IdempotencyKey</c>.
    /// </remarks>
    public sealed record MovimentarRequest(
    string IdempotencyKey,
    char Tipo,           // 'C' ou 'D'
    decimal Valor,
    int? NumeroConta     // opcional; se null, usar a conta do token
    );

    /// <summary>Movimentando conta (crédito/débito).</summary>
    /// <remarks>
    /// Usando a conta do token quando <c>NumeroConta</c> é omitido.  
    /// Idempotência por <c>IdempotencyKey</c>.
    ///
    /// <b>Exemplo (crédito na própria conta)</b>
    /// { "idempotencyKey": "mov-001", "tipo": "C", "valor": 100.00 }
    ///
    /// <b>Exemplo (crédito em outra conta)</b>
    /// { "idempotencyKey": "mov-002", "tipo": "C", "valor": 50.00, "numeroConta": 100001 }
    ///
    /// <b>204 No Content</b> — operação concluída
    ///
    /// <b>400 BadRequest</b>
    /// { "message": "Valor deve ser positivo.", "type": "INVALID_VALUE" }
    ///
    /// <b>403 Forbidden</b>
    /// { "message": "Token inválido.", "type": "INVALID_ACCOUNT" }
    /// </remarks>
    /// <param name="req">Dados da movimentação.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="204">Movimentação concluída.</response>
    /// <response code="400">
    /// Erros de regra: <c>INVALID_ACCOUNT</c>, <c>INACTIVE_ACCOUNT</c>, <c>INVALID_VALUE</c>, <c>INVALID_TYPE</c>.
    /// </response>
    /// <response code="403">Token inválido/expirado.</response>
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
    /// <summary>Consultando saldo.</summary>
    /// <remarks>
    /// Calculando saldo = soma(C) − soma(D).
    ///
    /// <b>200 OK</b>
    /// {
    ///   "numeroConta": 100000,
    ///   "nomeTitular": "Titular",
    ///   "dataHora": "2025-08-15T12:30:10-03:00",
    ///   "valor": 123.45
    /// }
    ///
    /// <b>400 BadRequest</b>
    /// { "message": "Conta inativa.", "type": "INACTIVE_ACCOUNT" }
    ///
    /// <b>403 Forbidden</b>
    /// { "message": "Token inválido.", "type": "INVALID_ACCOUNT" }
    /// </remarks>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="200">Retornando número, titular, data/hora e valor do saldo.</response>
    /// <response code="400"><c>INVALID_ACCOUNT</c> ou <c>INACTIVE_ACCOUNT</c>.</response>
    /// <response code="403">Token inválido/expirado.</response>
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
