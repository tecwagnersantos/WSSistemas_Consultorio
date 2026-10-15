using System.Security.Claims;
using Consultorio.Application.Abstractions;
using Consultorio.Domain.Common;

namespace Consultorio.Web.Infra;

/// <summary>
/// Lê o usuário logado a partir das claims do cookie de autenticação.
/// Telas das clínicas: cookie "Consultorio.Auth" (NameIdentifier, clinica_id...).
/// Área /Admin: cookie "Consultorio.Admin" (admin_id), que não carrega clínica.
/// </summary>
public class UsuarioAtual(IHttpContextAccessor accessor) : IUsuarioAtual
{
    public const string ClaimMedicoId = "medico_id";
    public const string ClaimClinicaId = "clinica_id";
    public const string ClaimClinicaNome = "clinica_nome";
    public const string ClaimAdminId = "admin_id";
    /// <summary>HttpContext.Items: último dia de acesso da clínica em período de teste (preenchido ao validar o cookie).</summary>
    public const string ItemAcessoAte = "acesso_ate";
    /// <summary>HttpContext.Items: mensalidade vencendo ou vencida (AvisoCobrancaDto), só para quem administra a clínica.</summary>
    public const string ItemCobranca = "cobranca";

    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public int? UsuarioId => int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public int? ClinicaId => int.TryParse(User?.FindFirstValue(ClaimClinicaId), out var id) ? id : null;
    public int? AdministradorSistemaId => int.TryParse(User?.FindFirstValue(ClaimAdminId), out var id) ? id : null;
    public int? MedicoId => int.TryParse(User?.FindFirstValue(ClaimMedicoId), out var id) ? id : null;
    public bool Pode(string transacao) => User?.HasClaim(Transacoes.Claim, transacao) ?? false;
    public string? EnderecoIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
