using Consultorio.Application.Admin;
using Consultorio.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

public class HomeController(AdminRelatorioService relatorios, CobrancaService cobrancas) : AdminBaseController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var iniMes = new DateOnly(hoje.Year, hoje.Month, 1);
        ViewBag.Cobrancas = await cobrancas.ResumoAsync(iniMes, iniMes.AddMonths(1).AddDays(-1), ct);
        ViewBag.Atrasadas = await cobrancas.ListarAsync(null, StatusCobranca.Atrasada, null, null, ct);
        return View(await relatorios.DashboardAsync(ct));
    }
}
