using System.Security.Claims;
using Consultorio.Web.Controllers;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Base da área /Admin: só entra quem tem o cookie de administrador do sistema.</summary>
[Area(AdminAuth.Area), Authorize(Policy = AdminAuth.Politica)]
public abstract class AdminBaseController : BaseController
{
    protected int AdminId => int.Parse(User.FindFirstValue(Infra.UsuarioAtual.ClaimAdminId)!);

    protected static string? Acesso(string? a) => a is "teste" or "definitivo" or "vencidos" ? a : null;
    /// <summary>Médicos e usuários: "ativos" → true, "inativos" → false, vazio → todos.</summary>
    protected static bool? Situacao(string? s) => s switch { "ativos" => true, "suspensos" or "inativos" => false, _ => null };

    /// <summary>
    /// Clientes: "ativos", "bloqueados", "inativos", "todos"; vazio = clientes atuais (ativos e bloqueados),
    /// para os ex-clientes não poluírem a lista do dia a dia.
    /// </summary>
    protected static string? SituacaoCliente(string? s) => s switch
    {
        "ativos" => Domain.Entities.SituacoesCliente.Ativo,
        "bloqueados" or "suspensos" => Domain.Entities.SituacoesCliente.Bloqueado,
        "inativos" => Domain.Entities.SituacoesCliente.Inativo,
        "todos" => null,
        _ => Application.Admin.AdminRelatorioService.SituacaoAtuais
    };
}
