using Consultorio.Application.Auth;
using Consultorio.Application.Cadastros;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Web.Models;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

/// <summary>Especialidades, médicos (com grade de horários) e convênios/planos.</summary>
public class CadastrosController(CadastroService cadastros, UsuarioService usuarios, LimitesPlano limites) : BaseController
{
    public IActionResult Index() =>
        User.Pode(Transacoes.CadastrosMedicos) ? RedirectToAction(nameof(Medicos))
        : User.Pode(Transacoes.CadastrosEspecialidades) ? RedirectToAction(nameof(Especialidades))
        : User.Pode(Transacoes.CadastrosConvenios) ? RedirectToAction(nameof(Convenios))
        : Forbid();

    // ---------- Especialidades ----------
    [HttpGet, Authorize(Policy = Transacoes.CadastrosEspecialidades)]
    public async Task<IActionResult> Especialidades(CancellationToken ct) =>
        View(await cadastros.ListarEspecialidadesAsync(ct));

    [HttpPost, Authorize(Policy = Transacoes.CadastrosEspecialidades)]
    public async Task<IActionResult> SalvarEspecialidade(EspecialidadeFormVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Erro"] = T["Informe o nome da especialidade."].Value;
            return RedirectToAction(nameof(Especialidades));
        }
        await cadastros.SalvarEspecialidadeAsync(vm.Id is > 0 ? vm.Id : null,
            new EspecialidadeDto(vm.Id ?? 0, vm.Nome, vm.CodigoCbo, vm.Ativo), ct);
        Sucesso("Especialidade salva.");
        return RedirectToAction(nameof(Especialidades));
    }

    // ---------- Médicos ----------
    [HttpGet, Authorize(Policy = Transacoes.CadastrosMedicos)]
    public async Task<IActionResult> Medicos(CancellationToken ct)
    {
        ViewBag.Uso = await limites.UsoAsync(UsuarioAtual.ClinicaId!.Value, ct);
        return View(await cadastros.ListarMedicosAsync(false, ct));
    }

    [HttpGet, Authorize(Policy = Transacoes.CadastrosMedicos)]
    public async Task<IActionResult> Medico(int? id, CancellationToken ct)
    {
        var vm = new MedicoFormVM();
        if (id is int mid)
        {
            var m = (await cadastros.ListarMedicosAsync(false, ct)).FirstOrDefault(x => x.Id == mid)
                    ?? throw new NaoEncontradoException("Médico não encontrado.");
            vm = new MedicoFormVM
            {
                Id = m.Id, UsuarioId = m.UsuarioId, Nome = m.Nome, Crm = m.Crm, UfCrm = m.UfCrm, Cpf = m.Cpf,
                Email = m.Email, Telefone = m.Telefone, DuracaoConsultaPadraoMin = m.DuracaoConsultaPadraoMin, Ativo = m.Ativo,
                PrincipalId = m.Especialidades.FirstOrDefault(e => e.Principal)?.EspecialidadeId
            };
            await CompletarMedicoAsync(vm, m.Especialidades, ct);
        }
        else await CompletarMedicoAsync(vm, [], ct);
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.CadastrosMedicos)]
    public async Task<IActionResult> Medico(MedicoFormVM vm, CancellationToken ct)
    {
        if (!vm.Especialidades.Any(e => e.Selecionada))
            ModelState.AddModelError("", T["Selecione ao menos uma especialidade."]);
        if (vm.PrincipalId is null || !vm.Especialidades.Any(e => e.Selecionada && e.EspecialidadeId == vm.PrincipalId))
            vm.PrincipalId = vm.Especialidades.FirstOrDefault(e => e.Selecionada)?.EspecialidadeId;

        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () => id = await cadastros.SalvarMedicoAsync(vm.Id, vm.ParaDto(), ct)))
        {
            Sucesso("Médico salvo.");
            return RedirectToAction(nameof(Medicos));
        }
        vm.UsuariosMedicos = await UsuariosMedicosAsync(ct);
        return View(vm);
    }

    [HttpGet, Authorize(Policy = Transacoes.CadastrosMedicos)]
    public async Task<IActionResult> Horarios(int id, CancellationToken ct)
    {
        var medico = (await cadastros.ListarMedicosAsync(false, ct)).FirstOrDefault(x => x.Id == id)
                     ?? throw new NaoEncontradoException("Médico não encontrado.");
        return View(new HorariosVM
        {
            Medico = medico,
            Horarios = await cadastros.ListarHorariosAsync(id, ct),
            Especialidades = await cadastros.ListarEspecialidadesAsync(ct)
        });
    }

    [HttpPost, Authorize(Policy = Transacoes.CadastrosMedicos)]
    public async Task<IActionResult> SalvarHorario(HorarioFormVM vm, CancellationToken ct)
    {
        if (vm.DiasSemana.Count == 0) ModelState.AddModelError("", T["Marque ao menos um dia da semana."]);
        if (!ModelState.IsValid)
        {
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Horarios), new { id = vm.MedicoId });
        }

        var dias = vm.Id is null ? vm.DiasSemana : vm.DiasSemana.Take(1).ToList();
        foreach (var dia in dias)
        {
            await cadastros.SalvarHorarioAsync(vm.Id, new HorarioAtendimentoDto(vm.Id ?? 0, vm.MedicoId, vm.EspecialidadeId, dia,
                vm.HoraInicio!.Value, vm.HoraFim!.Value, vm.DuracaoSlotMin, vm.VigenciaInicio!.Value, vm.VigenciaFim, vm.Ativo), ct);
        }
        Sucesso(vm.Id is null ? "Horários incluídos." : "Horário atualizado.");
        return RedirectToAction(nameof(Horarios), new { id = vm.MedicoId });
    }

    // ---------- Convênios ----------
    [HttpGet, Authorize(Policy = Transacoes.CadastrosConvenios)]
    public async Task<IActionResult> Convenios(CancellationToken ct) =>
        View(await cadastros.ListarConveniosAsync(ct));

    [HttpGet, Authorize(Policy = Transacoes.CadastrosConvenios)]
    public async Task<IActionResult> Convenio(int? id, CancellationToken ct)
    {
        var vm = new ConvenioFormVM { Planos = [new()] };
        if (id is int cid)
        {
            var c = (await cadastros.ListarConveniosAsync(ct)).FirstOrDefault(x => x.Id == cid)
                    ?? throw new NaoEncontradoException("Convênio não encontrado.");
            vm = new ConvenioFormVM
            {
                Id = c.Id, Nome = c.Nome, RegistroAns = c.RegistroAns, Cnpj = c.Cnpj, Telefone = c.Telefone,
                PrazoRetornoDias = c.PrazoRetornoDias, Ativo = c.Ativo,
                Planos = c.Planos.Select(p => new PlanoFormVM { Id = p.Id, Nome = p.Nome, Codigo = p.Codigo, Ativo = p.Ativo }).ToList()
            };
        }
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.CadastrosConvenios)]
    public async Task<IActionResult> Convenio(ConvenioFormVM vm, CancellationToken ct)
    {
        if (ModelState.IsValid && await TentarAsync(() => cadastros.SalvarConvenioAsync(vm.Id, vm.ParaDto(), ct)))
        {
            Sucesso("Convênio salvo.");
            return RedirectToAction(nameof(Convenios));
        }
        return View(vm);
    }

    private async Task CompletarMedicoAsync(MedicoFormVM vm, List<MedicoEspecialidadeDto> atuais, CancellationToken ct)
    {
        var todas = await cadastros.ListarEspecialidadesAsync(ct);
        vm.Especialidades = todas.Where(e => e.Ativo || atuais.Any(a => a.EspecialidadeId == e.Id))
            .Select(e =>
            {
                var atual = atuais.FirstOrDefault(a => a.EspecialidadeId == e.Id);
                return new MedicoEspecialidadeFormVM { EspecialidadeId = e.Id, Nome = e.Nome, Selecionada = atual is not null, Rqe = atual?.Rqe };
            }).ToList();
        vm.UsuariosMedicos = await UsuariosMedicosAsync(ct);
    }

    /// <summary>Usuários que podem ser o "usuário de acesso" de um médico: os que registram no prontuário.</summary>
    private async Task<List<UsuarioDto>> UsuariosMedicosAsync(CancellationToken ct) =>
        await usuarios.ListarComTransacaoAsync(Transacoes.ProntuarioEditar, ct);
}
