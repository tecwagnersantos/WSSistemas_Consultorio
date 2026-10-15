using Consultorio.Application.Admin;
using Consultorio.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Perfis de acesso: o que cada perfil pode ver e fazer nas clínicas (transações).</summary>
public class PerfisController(PerfilAcessoService perfis, AdminRelatorioService relatorios) : AdminBaseController
{
    public async Task<IActionResult> Index(int? clinicaId, CancellationToken ct) =>
        View(new PerfisVM
        {
            ClinicaId = clinicaId,
            Itens = await perfis.ListarAsync(clinicaId, ct),
            Clientes = await relatorios.ClientesAsync(null, ct)
        });

    [HttpGet]
    public async Task<IActionResult> Form(int? id, int? clinicaId, CancellationToken ct)
    {
        var vm = new PerfilFormVM { ClinicaId = clinicaId };
        if (id is int pid)
        {
            var p = await perfis.ObterAsync(pid, ct);
            vm = new PerfilFormVM { Id = p.Id, Nome = p.Nome, Descricao = p.Descricao, ClinicaId = p.ClinicaId, Ativo = p.Ativo, Transacoes = p.Transacoes.ToList() };
        }
        vm.Clientes = await relatorios.ClientesAsync(null, ct);
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Form(PerfilFormVM vm, CancellationToken ct)
    {
        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () => id = await perfis.SalvarAsync(vm.Id,
                new PerfilSalvarRequest(vm.Nome, vm.Descricao, vm.ClinicaId, vm.Ativo, vm.Transacoes), ct)))
        {
            Sucesso("Perfil salvo. Os usuários com este perfil passam a ter o novo acesso na próxima página que abrirem.");
            return RedirectToAction(nameof(Index), new { clinicaId = vm.ClinicaId });
        }
        vm.Clientes = await relatorios.ClientesAsync(null, ct);
        return View(vm);
    }
}
