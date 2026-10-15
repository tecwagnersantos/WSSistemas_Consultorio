using Consultorio.Application.Cadastros;
using Consultorio.Application.Relatorios;
using Consultorio.Domain.Common;
using Consultorio.Web.Infra;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

public class HomeController(IRelatorioQueries relatorios, CadastroService cadastros) : BaseController
{
    /// <summary>Página inicial: dashboard para quem tem a transação; os demais vão para a primeira tela liberada.</summary>
    public async Task<IActionResult> Index(int? medicoId, CancellationToken ct)
    {
        if (!User.Pode(Transacoes.DashboardVer))
        {
            if (User.Pode(Transacoes.AgendaVer)) return RedirectToAction("Index", "Agenda");
            if (User.Pode(Transacoes.PacientesVer)) return RedirectToAction("Index", "Pacientes");
            if (User.Pode(Transacoes.RelatoriosVer)) return RedirectToAction("Index", "Relatorios");
            if (User.PodeAlgum(Transacoes.CadastrosMedicos, Transacoes.CadastrosEspecialidades, Transacoes.CadastrosConvenios)) return RedirectToAction("Index", "Cadastros");
            if (User.Pode(Transacoes.UsuariosGerenciar)) return RedirectToAction("Index", "Usuarios");
            return View("SemAcesso");
        }

        var podeEscolher = User.Pode(Transacoes.RelatoriosTodosMedicos);
        var filtro = podeEscolher ? medicoId : UsuarioAtual.MedicoId ?? -1;
        var vm = new DashboardVM
        {
            Dados = await relatorios.DashboardAsync(filtro, ct),
            MedicoId = filtro,
            PodeEscolherMedico = podeEscolher,
            Medicos = podeEscolher ? await cadastros.ListarMedicosAsync(true, ct) : []
        };
        return View("Dashboard", vm);
    }

    [AllowAnonymous]
    public IActionResult Erro()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
        ViewBag.Mensagem = feature?.Error is { } ex ? TratamentoErrosFilter.MensagemAmigavel(ex, T, out _) : null;
        return View();
    }
}
