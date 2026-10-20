using System.Security.Claims;
using Consultorio.Application.Auth;
using Consultorio.Web.Infra;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consultorio.Web.Controllers;

public class ContaController(AuthService auth) : BaseController
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        UsuarioLogadoDto? usuario = null;
        if (!await TentarAsync(async () => usuario = await auth.LoginAsync(new LoginRequest(vm.Email, vm.Senha), ct)))
        {
            vm.Senha = "";
            return View(vm);
        }

        await EntrarAsync(usuario!);
        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Cadastro de demonstração: clínica em período de teste + usuário administrador (e médico, se informar o CRM).</summary>
    [HttpGet, AllowAnonymous]
    public IActionResult Demonstracao()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        if (!ConfigDemonstracao.Habilitado) return RedirectToAction(nameof(Login));
        ViewBag.Dias = ConfigDemonstracao.Dias;
        return View(new DemonstracaoVM());
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Demonstracao(DemonstracaoVM vm, [FromServices] DemonstracaoService demonstracao, CancellationToken ct)
    {
        var config = ConfigDemonstracao;
        if (!config.Habilitado) return RedirectToAction(nameof(Login));
        if (vm.SouMedico && (string.IsNullOrWhiteSpace(vm.Crm) || string.IsNullOrWhiteSpace(vm.UfCrm)))
            ModelState.AddModelError(nameof(vm.Crm), T["Informe o CRM e a UF do CRM, ou desmarque \"Sou médico\"."]);

        UsuarioLogadoDto? usuario = null;
        if (ModelState.IsValid && await TentarAsync(async () =>
            {
                await demonstracao.CadastrarAsync(new DemonstracaoRequest(vm.NomeClinica, vm.Telefone, vm.Cidade, vm.Uf,
                    vm.NomeResponsavel, vm.Email, vm.Senha, vm.SouMedico, vm.Crm, vm.UfCrm), config, ct);
                usuario = await auth.LoginAsync(new LoginRequest(vm.Email, vm.Senha), ct);
            }))
        {
            await EntrarAsync(usuario!);
            Sucesso("Bem-vindo(a)! Sua demonstração vale por {0} dias.", config.Dias);
            return RedirectToAction("Index", "Home");
        }
        vm.Senha = vm.Confirmacao = "";
        ViewBag.Dias = config.Dias;
        return View(vm);
    }

    private DemonstracaoConfig ConfigDemonstracao =>
        HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("Demonstracao").Get<DemonstracaoConfig>() ?? new();

    private async Task EntrarAsync(UsuarioLogadoDto usuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nome),
            new(ClaimTypes.Email, usuario.Email)
        };
        claims.AddRange(usuario.Perfis.Select(p => new Claim(ClaimTypes.Role, p)));
        if (usuario.MedicoId is int medicoId) claims.Add(new Claim(Infra.UsuarioAtual.ClaimMedicoId, medicoId.ToString()));
        // Clínica (cliente) do usuário: usada nos filtros globais do EF Core e nos relatórios
        if (usuario.ClinicaId is int clinicaId)
        {
            claims.Add(new Claim(Infra.UsuarioAtual.ClaimClinicaId, clinicaId.ToString()));
            claims.Add(new Claim(Infra.UsuarioAtual.ClaimClinicaNome, usuario.ClinicaNome ?? ""));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        // Idioma gravado no usuário vale em qualquer computador
        if (Idiomas.Normalizar(usuario.Idioma) is { } idioma) IdiomaCookie.Definir(Response, idioma);
    }

    [HttpPost]
    public async Task<IActionResult> Sair(CancellationToken ct)
    {
        if (UsuarioAtual.UsuarioId is int id) await auth.RegistrarLogoutAsync(id, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AcessoNegado() => View();

    [HttpGet]
    public IActionResult AlterarSenha() => View(new AlterarSenhaVM());

    [HttpPost]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        if (!await TentarAsync(() => auth.AlterarSenhaAsync(UsuarioAtual.UsuarioId!.Value, vm.SenhaAtual, vm.NovaSenha, ct)))
            return View(new AlterarSenhaVM());
        Sucesso("Senha alterada com sucesso.");
        return RedirectToAction("Index", "Home");
    }
}
