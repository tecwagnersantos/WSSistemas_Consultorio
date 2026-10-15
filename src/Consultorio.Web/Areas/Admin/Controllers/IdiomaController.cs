using System.Security.Claims;
using Consultorio.Application.Admin;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Troca o idioma na área /Admin (também na tela de login). Logado, a escolha fica gravada no administrador.</summary>
public class IdiomaController(AdminAuthService admins) : AdminBaseController
{
    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Definir(string? idioma, string? voltar, CancellationToken ct)
    {
        var codigo = Idiomas.Normalizar(idioma) ?? Idiomas.Padrao;
        IdiomaCookie.Definir(Response, codigo);
        var admin = await HttpContext.AuthenticateAsync(AdminAuth.Esquema);
        if (admin.Succeeded && int.TryParse(admin.Principal.FindFirstValue(Infra.UsuarioAtual.ClaimAdminId), out var adminId))
            await admins.DefinirIdiomaAsync(adminId, codigo, ct);
        return !string.IsNullOrEmpty(voltar) && Url.IsLocalUrl(voltar) ? LocalRedirect(voltar) : RedirectToAction("Index", "Home");
    }
}
