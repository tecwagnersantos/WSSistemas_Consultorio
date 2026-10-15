using System.Security.Claims;
using Consultorio.Application.Admin;
using Consultorio.Application.Auth;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

/// <summary>Troca o idioma das telas da clínica. Logado, a escolha fica gravada no usuário. A área /Admin tem o seu (Areas/Admin/Controllers/IdiomaController).</summary>
[AllowAnonymous]
public class IdiomaController(AuthService auth) : BaseController
{
    [HttpPost]
    public async Task<IActionResult> Definir(string? idioma, string? voltar, CancellationToken ct)
    {
        var codigo = Idiomas.Normalizar(idioma) ?? Idiomas.Padrao;
        IdiomaCookie.Definir(Response, codigo);

        if (UsuarioAtual.UsuarioId is int usuarioId && UsuarioAtual.ClinicaId is int clinicaId)
            await auth.DefinirIdiomaAsync(usuarioId, clinicaId, codigo, ct);

        return !string.IsNullOrEmpty(voltar) && Url.IsLocalUrl(voltar) ? LocalRedirect(voltar) : Redirect("~/");
    }
}
