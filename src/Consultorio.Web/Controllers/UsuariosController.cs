using Consultorio.Application.Admin;
using Consultorio.Application.Auth;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

[Authorize(Policy = Transacoes.UsuariosGerenciar)]
public class UsuariosController(UsuarioService usuarios, LimitesPlano limites, PerfilAcessoService perfis) : BaseController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Uso = await limites.UsoAsync(UsuarioAtual.ClinicaId!.Value, ct);
        return View(await usuarios.ListarAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Form(int? id, CancellationToken ct)
    {
        var vm = new UsuarioFormVM();
        if (id is int uid)
        {
            var u = (await usuarios.ListarAsync(ct)).FirstOrDefault(x => x.Id == uid)
                    ?? throw new NaoEncontradoException("Usuário não encontrado.");
            vm = new UsuarioFormVM { Id = u.Id, Nome = u.Nome, Email = u.Email, Ativo = u.Ativo, PerfilIds = u.PerfilIds.ToList() };
        }
        vm.Opcoes = await perfis.OpcoesAsync(UsuarioAtual.ClinicaId!.Value, ct);
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Form(UsuarioFormVM vm, CancellationToken ct)
    {
        if (vm.Id == UsuarioAtual.UsuarioId)
        {
            if (!vm.Ativo) ModelState.AddModelError("", T["Você não pode desativar o próprio usuário."]);
            // Quem gerencia usuários não pode tirar de si mesmo esse acesso (ficaria sem como desfazer)
            var gerenciaria = await perfis.LiberaAsync(vm.PerfilIds, Transacoes.UsuariosGerenciar, ct);
            if (!gerenciaria) ModelState.AddModelError("", T["Mantenha em você um perfil que gerencia usuários; senão você perde o acesso a esta tela."]);
        }

        if (ModelState.IsValid && await TentarAsync(() =>
                usuarios.SalvarAsync(vm.Id, new UsuarioSalvarRequest(vm.Nome, vm.Email, vm.Senha, vm.Ativo, vm.PerfilIds), ct)))
        {
            Sucesso("Usuário salvo.");
            return RedirectToAction(nameof(Index));
        }
        vm.Senha = null;
        vm.Opcoes = await perfis.OpcoesAsync(UsuarioAtual.ClinicaId!.Value, ct);
        return View(vm);
    }
}
