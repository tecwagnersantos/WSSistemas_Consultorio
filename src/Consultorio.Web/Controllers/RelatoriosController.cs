using System.Globalization;
using Consultorio.Application.Cadastros;
using Consultorio.Application.Relatorios;
using Consultorio.Domain.Common;
using Consultorio.Web.Infra;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

/// <summary>Relatórios: o Administrador vê todos os médicos; o Médico, apenas os próprios números.</summary>
[Authorize(Policy = Transacoes.RelatoriosVer)]
public class RelatoriosController(IRelatorioQueries queries, CadastroService cadastros) : BaseController
{
    public IActionResult Index() => RedirectToAction(nameof(Atendimentos));

    public async Task<IActionResult> Atendimentos(DateOnly? de, DateOnly? ate, int? medicoId, string? status, string? formato, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var vm = new AtendimentosVM
        {
            De = de ?? new DateOnly(hoje.Year, hoje.Month, 1),
            Ate = ate ?? hoje,
            MedicoId = Filtro(medicoId),
            Status = string.IsNullOrEmpty(status) ? null : status,
            PodeEscolherMedico = User.Pode(Transacoes.RelatoriosTodosMedicos)
        };
        vm.Itens = await queries.AtendimentosAsync(vm.De, vm.Ate, vm.MedicoId, vm.Status, ct);

        if (formato == "csv")
        {
            var linhas = vm.Itens.Select(a => new object?[]
            {
                a.Inicio.ToString("dd/MM/yyyy HH:mm"), Exibicao.NomeStatusRelatorio(a.Status), Exibicao.NomeTipoRelatorio(a.TipoAtendimento),
                a.Medico, a.Especialidade, a.Paciente, a.Convenio ?? "Particular", a.Plano, a.Valor, a.EsperaMin
            });
            return Csv($"atendimentos_{vm.De:yyyyMMdd}_{vm.Ate:yyyyMMdd}.csv",
                ["Data/hora", "Status", "Tipo", "Médico", "Especialidade", "Paciente", "Convênio", "Plano", "Valor", "Espera (min)"], linhas);
        }

        if (vm.PodeEscolherMedico) vm.Medicos = await cadastros.ListarMedicosAsync(false, ct);
        return View(vm);
    }

    /// <param name="de">Mês inicial no formato yyyy-MM (input type="month").</param>
    /// <param name="ate">Mês final no formato yyyy-MM.</param>
    public async Task<IActionResult> Indicadores(string? de, string? ate, int? medicoId, string? formato, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var vm = new IndicadoresVM
        {
            De = Mes(de) ?? new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-5),
            Ate = Mes(ate) ?? new DateOnly(hoje.Year, hoje.Month, 1),
            MedicoId = Filtro(medicoId),
            PodeEscolherMedico = User.Pode(Transacoes.RelatoriosTodosMedicos)
        };
        // A view agrupa por mês: normaliza para o primeiro dia
        vm.De = new DateOnly(vm.De.Year, vm.De.Month, 1);
        vm.Ate = new DateOnly(vm.Ate.Year, vm.Ate.Month, 1);
        vm.Itens = await queries.IndicadoresMensaisAsync(vm.De, vm.Ate, vm.MedicoId, ct);

        if (formato == "csv")
        {
            var linhas = vm.Itens.Select(i => new object?[]
            {
                i.Mes.ToString("MM/yyyy"), i.Medico, i.TotalAgendamentos, i.Atendidos, i.Faltas, i.Cancelados, i.Particulares, i.ValorAtendido
            });
            return Csv($"indicadores_{vm.De:yyyyMM}_{vm.Ate:yyyyMM}.csv",
                ["Mês", "Médico", "Agendamentos", "Atendidos", "Faltas", "Cancelados", "Particulares", "Valor atendido"], linhas);
        }

        if (vm.PodeEscolherMedico) vm.Medicos = await cadastros.ListarMedicosAsync(false, ct);
        return View(vm);
    }

    private static DateOnly? Mes(string? valor) =>
        DateOnly.TryParseExact(valor + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private int? Filtro(int? medicoId) => User.Pode(Transacoes.RelatoriosTodosMedicos) ? medicoId : UsuarioAtual.MedicoId ?? -1;
}
