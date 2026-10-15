using System.Security.Claims;
using Consultorio.Application.Admin;
using Consultorio.Web.Infra;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Login separado da área admin (não usa o login das clínicas).</summary>
public class ContaController(AdminAuthService auth) : AdminBaseController
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        if ((await HttpContext.AuthenticateAsync(AdminAuth.Esquema)).Succeeded) return RedirectToAction("Index", "Home");
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        AdminLogadoDto? admin = null;
        if (!await TentarAsync(async () => admin = await auth.LoginAsync(vm.Email, vm.Senha, ct)))
        {
            vm.Senha = "";
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(Infra.UsuarioAtual.ClaimAdminId, admin!.Id.ToString()),
            new(ClaimTypes.Name, admin.Nome),
            new(ClaimTypes.Email, admin.Email)
        };
        await HttpContext.SignInAsync(AdminAuth.Esquema, new ClaimsPrincipal(new ClaimsIdentity(claims, AdminAuth.Esquema)));
        if (Idiomas.Normalizar(admin.Idioma) is { } idioma) IdiomaCookie.Definir(Response, idioma);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Sair(CancellationToken ct)
    {
        await auth.RegistrarLogoutAsync(AdminId, ct);
        await HttpContext.SignOutAsync(AdminAuth.Esquema);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AlterarSenha() => View(new AlterarSenhaVM());

    [HttpPost]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        if (!await TentarAsync(() => auth.AlterarSenhaAsync(AdminId, vm.SenhaAtual, vm.NovaSenha, ct)))
            return View(new AlterarSenhaVM());
        Sucesso("Senha alterada com sucesso.");
        return RedirectToAction("Index", "Home");
    }
}
