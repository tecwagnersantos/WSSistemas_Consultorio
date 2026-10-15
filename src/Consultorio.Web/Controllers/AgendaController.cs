using Consultorio.Application.Agenda;
using Consultorio.Application.Cadastros;
using Consultorio.Domain.Common;
using Consultorio.Web.Infra;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

[Authorize(Policy = Transacoes.AgendaVer)]
public class AgendaController(AgendaService agenda, CadastroService cadastros) : BaseController
{
    /// <summary>Agenda do dia ou da semana. O médico (sem outro perfil) vê só a própria agenda.</summary>
    public async Task<IActionResult> Index(DateOnly? data, int? medicoId, string? visao, CancellationToken ct)
    {
        var dia = data ?? DateOnly.FromDateTime(DateTime.Today);
        var semana = visao == "semana";
        var de = semana ? dia.AddDays(-(int)dia.DayOfWeek) : dia;
        var ate = semana ? de.AddDays(6) : dia;
        var podeEscolher = User.Pode(Transacoes.AgendaTodosMedicos);

        var vm = new AgendaVM
        {
            Data = dia, Visao = semana ? "semana" : "dia", De = de, Ate = ate,
            MedicoId = podeEscolher ? medicoId : UsuarioAtual.MedicoId,
            PodeEscolherMedico = podeEscolher,
            Medicos = await cadastros.ListarMedicosAsync(true, ct),
            Itens = await agenda.ListarAsync(de, ate, medicoId, ct),
            Bloqueios = await agenda.ListarBloqueiosAsync(de, ate, ct)
        };
        if (vm.MedicoId is int m) vm.Bloqueios = vm.Bloqueios.Where(b => b.MedicoId == null || b.MedicoId == m).ToList();
        return View(vm);
    }

    [HttpGet, Authorize(Policy = Transacoes.AgendaAgendar)]
    public async Task<IActionResult> Novo(DateOnly? data, int? medicoId, int? pacienteId, string? pacienteNome, CancellationToken ct)
    {
        var vm = new NovoAgendamentoVM
        {
            Dia = data ?? DateOnly.FromDateTime(DateTime.Today),
            MedicoId = medicoId ?? UsuarioAtual.MedicoId ?? 0,
            PacienteId = pacienteId ?? 0,
            PacienteNome = pacienteNome
        };
        vm.Medicos = await cadastros.ListarMedicosAsync(true, ct);
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.AgendaAgendar)]
    public async Task<IActionResult> Novo(NovoAgendamentoVM vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var inicio = vm.Dia!.Value.ToDateTime(vm.Hora!.Value);
            var req = new NovoAgendamentoRequest(vm.PacienteId, vm.MedicoId, vm.EspecialidadeId, vm.PacientePlanoId,
                vm.Tipo, inicio, vm.DuracaoMin, vm.Valor, vm.Observacoes);
            if (await TentarAsync(() => agenda.AgendarAsync(req, ct)))
            {
                Sucesso("Agendamento criado para {0}.", Exibicao.DataHora(inicio));
                return RedirectToAction(nameof(Index), new { data = vm.Dia.Value.ToString("yyyy-MM-dd"), medicoId = vm.MedicoId });
            }
        }
        vm.Medicos = await cadastros.ListarMedicosAsync(true, ct);
        return View(vm);
    }

    /// <summary>JSON: horários livres do médico no dia (a partir da grade de atendimento).</summary>
    [HttpGet]
    public async Task<IActionResult> HorariosLivres(int medicoId, DateOnly dia, CancellationToken ct)
    {
        var livres = await agenda.HorariosLivresAsync(medicoId, dia, ct);
        return Json(livres.Select(l => new { inicio = l.Inicio.ToString("HH:mm"), fim = l.Fim.ToString("HH:mm") }));
    }

    /// <summary>JSON: muda o status (confirmar, chegada, falta, cancelar...).</summary>
    [HttpPost, Authorize(Policy = Transacoes.AgendaAgendar)]
    public async Task<IActionResult> AlterarStatus(int id, [FromBody] AlterarStatusVM vm, CancellationToken ct)
    {
        await agenda.AlterarStatusAsync(id, new AlterarStatusRequest(vm.Status, vm.Observacao), ct);
        return JsonOk();
    }

    /// <summary>JSON: remarca para outro horário.</summary>
    [HttpPost, Authorize(Policy = Transacoes.AgendaAgendar)]
    public async Task<IActionResult> Remarcar(int id, [FromBody] RemarcarVM vm, CancellationToken ct)
    {
        var novoId = await agenda.RemarcarAsync(id, new RemarcarRequest(vm.NovoInicio, vm.DuracaoMin, vm.Observacao), ct);
        return JsonOk(new { ok = true, id = novoId });
    }

    [HttpGet]
    public async Task<IActionResult> Bloqueios(DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var ini = de ?? DateOnly.FromDateTime(DateTime.Today);
        var fim = ate ?? ini.AddDays(60);
        return View(new BloqueiosVM
        {
            De = ini, Ate = fim,
            Medicos = await cadastros.ListarMedicosAsync(false, ct),
            Itens = await agenda.ListarBloqueiosAsync(ini, fim, ct)
        });
    }

    [HttpPost, Authorize(Policy = Transacoes.AgendaBloqueios)]
    public async Task<IActionResult> Bloquear(BloqueioFormVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Bloqueios));
        }
        await agenda.BloquearAsync(new BloqueioDto(0, vm.MedicoId, vm.Inicio!.Value, vm.Fim!.Value, vm.Motivo), ct);
        Sucesso("Bloqueio registrado.");
        return RedirectToAction(nameof(Bloqueios));
    }
}
