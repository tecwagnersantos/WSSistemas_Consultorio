using Consultorio.Application.Admin;
using Consultorio.Application.Common;
using Consultorio.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Quem tem acesso à área admin (equipe de suporte/configuração do sistema).</summary>
public class AdministradoresController(AdminAuthService admins) : AdminBaseController
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await admins.ListarAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Form(int? id, CancellationToken ct)
    {
        if (id is not int aid) return View(new AdministradorFormVM());
        var a = (await admins.ListarAsync(ct)).FirstOrDefault(x => x.Id == aid) ?? throw new NaoEncontradoException("Administrador não encontrado.");
        return View(new AdministradorFormVM { Id = a.Id, Nome = a.Nome, Email = a.Email, Ativo = a.Ativo });
    }

    [HttpPost]
    public async Task<IActionResult> Form(AdministradorFormVM vm, CancellationToken ct)
    {
        if (vm.Id == AdminId && !vm.Ativo) ModelState.AddModelError("", T["Você não pode desativar o próprio acesso."]);
        if (ModelState.IsValid && await TentarAsync(() => admins.SalvarAsync(vm.Id, new AdministradorSalvarRequest(vm.Nome, vm.Email, vm.Senha, vm.Ativo), ct)))
        {
            Sucesso("Administrador salvo.");
            return RedirectToAction(nameof(Index));
        }
        vm.Senha = null;
        return View(vm);
    }
}
