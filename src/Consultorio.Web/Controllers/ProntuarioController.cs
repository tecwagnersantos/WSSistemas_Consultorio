using Consultorio.Application.Pacientes;
using Consultorio.Application.Prontuario;
using Consultorio.Domain.Common;
using Consultorio.Web.Models;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Consultorio.Web.Controllers;

/// <summary>Prontuário eletrônico: só com as transações de prontuário (perfil Médico, por padrão). Toda leitura e gravação é auditada (LGPD).</summary>
[Authorize(Policy = Transacoes.ProntuarioVer)]
[Route("Prontuario/{pacienteId:int}/[action]")]
public class ProntuarioController(ProntuarioService prontuario, AnexoService anexos, PacienteService pacientes) : BaseController
{
    /// <summary>Teto de um envio (vários arquivos). O limite por arquivo é Anexos:TamanhoMaximoMB; no IIS veja também o web.config.</summary>
    private const long LimiteEnvio = 100L * 1024 * 1024;

    [HttpGet("/Prontuario/{pacienteId:int}")]
    public async Task<IActionResult> Index(int pacienteId, int? agendamentoId, CancellationToken ct)
    {
        var vm = new ProntuarioVM
        {
            Paciente = await pacientes.ObterAsync(pacienteId, ct),
            Resumo = await prontuario.ObterResumoAsync(pacienteId, ct),
            Evolucoes = await prontuario.ListarEvolucoesAsync(pacienteId, ct),
            Prescricoes = await prontuario.ListarPrescricoesAsync(pacienteId, ct),
            Anexos = User.Pode(Transacoes.ProntuarioAnexos) ? await anexos.ListarAsync(pacienteId, ct) : [],
            MedicoLogadoId = UsuarioAtual.MedicoId,
            UsuarioLogadoId = UsuarioAtual.UsuarioId,
            AgendamentoId = agendamentoId
        };
        return View(vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> Resumo(int pacienteId, ResumoClinicoFormVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Erro"] = T["Verifique os campos do resumo clínico (textos muito longos)."].Value;
            return RedirectToAction(nameof(Index), new { pacienteId });
        }
        if (string.IsNullOrWhiteSpace(vm.TipoSanguineo)) vm.TipoSanguineo = null;
        await prontuario.SalvarResumoAsync(pacienteId, vm.ParaDto(), ct);
        Sucesso("Resumo clínico salvo.");
        return Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#resumo");
    }

    [HttpGet, Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> NovaEvolucao(int pacienteId, int? agendamentoId, int? retificaId, CancellationToken ct)
    {
        var vm = new EvolucaoFormVM { PacienteId = pacienteId, AgendamentoId = agendamentoId };
        if (retificaId is int r)
        {
            var original = await prontuario.ObterEvolucaoAsync(pacienteId, r, ct);
            vm = EvolucaoFormVM.De(original);
            vm.Id = null;
            vm.EvolucaoRetificadaId = r;
            vm.Retificada = original;
        }
        await CompletarAsync(vm, ct);
        return View("Evolucao", vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> NovaEvolucao(int pacienteId, EvolucaoFormVM vm, CancellationToken ct)
    {
        vm.PacienteId = pacienteId;
        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () =>
            {
                id = await prontuario.CriarEvolucaoAsync(pacienteId, vm.ParaRequest(), ct);
                if (vm.Acao == "assinar") await prontuario.AssinarAsync(id, ct);
            }))
        {
            if (vm.Acao == "assinar")
            {
                Sucesso("Evolução registrada e assinada.");
                return Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#evolucoes");
            }
            Sucesso("Rascunho salvo. Assine a evolução para concluir o atendimento.");
            return RedirectToAction(nameof(EditarEvolucao), new { pacienteId, id });
        }
        if (id > 0)
        {
            // O rascunho foi gravado, mas a assinatura falhou: continua a partir do rascunho
            TempData["Erro"] = T["Rascunho salvo, mas não foi possível assinar:"] + " " +
                string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(EditarEvolucao), new { pacienteId, id });
        }
        await CompletarAsync(vm, ct);
        return View("Evolucao", vm);
    }

    [HttpGet("{id:int}"), Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> EditarEvolucao(int pacienteId, int id, CancellationToken ct)
    {
        var e = await prontuario.ObterEvolucaoAsync(pacienteId, id, ct);
        if (e.Situacao == "A")
        {
            TempData["Erro"] = T["Evolução assinada não pode ser alterada. Use \"Retificar\"."].Value;
            return RedirectToAction(nameof(Index), new { pacienteId });
        }
        var vm = EvolucaoFormVM.De(e);
        await CompletarAsync(vm, ct);
        return View("Evolucao", vm);
    }

    [HttpPost("{id:int}"), Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> EditarEvolucao(int pacienteId, int id, EvolucaoFormVM vm, CancellationToken ct)
    {
        vm.PacienteId = pacienteId;
        vm.Id = id;
        if (ModelState.IsValid && await TentarAsync(async () =>
            {
                await prontuario.AtualizarRascunhoAsync(id, vm.ParaRequest(), ct);
                if (vm.Acao == "assinar") await prontuario.AssinarAsync(id, ct);
            }))
        {
            Sucesso(vm.Acao == "assinar" ? "Evolução assinada." : "Rascunho salvo.");
            return vm.Acao == "assinar"
                ? Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#evolucoes")
                : RedirectToAction(nameof(EditarEvolucao), new { pacienteId, id });
        }
        await CompletarAsync(vm, ct);
        return View("Evolucao", vm);
    }

    [HttpPost("{id:int}"), Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> Assinar(int pacienteId, int id, CancellationToken ct)
    {
        await prontuario.AssinarAsync(id, ct);
        Sucesso("Evolução assinada.");
        return Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#evolucoes");
    }

    [HttpGet, Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> NovaPrescricao(int pacienteId, int? evolucaoId, CancellationToken ct)
    {
        var vm = new PrescricaoFormVM { PacienteId = pacienteId, EvolucaoId = evolucaoId, Itens = [new()] };
        vm.PacienteNome = await NomePacienteAsync(pacienteId, ct);
        return View("Prescricao", vm);
    }

    [HttpPost, Authorize(Policy = Transacoes.ProntuarioEditar)]
    public async Task<IActionResult> NovaPrescricao(int pacienteId, PrescricaoFormVM vm, CancellationToken ct)
    {
        vm.PacienteId = pacienteId;
        var dto = vm.ParaDto();
        if (dto.Itens.Any(i => string.IsNullOrWhiteSpace(i.Posologia)))
            ModelState.AddModelError("", T["Informe a posologia de todos os medicamentos."]);

        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () => id = await prontuario.EmitirPrescricaoAsync(pacienteId, dto, ct)))
        {
            Sucesso("Prescrição emitida.");
            return RedirectToAction(nameof(Receita), new { pacienteId, id });
        }
        if (vm.Itens.Count == 0) vm.Itens.Add(new());
        vm.PacienteNome = await NomePacienteAsync(pacienteId, ct);
        return View("Prescricao", vm);
    }

    /// <summary>Receita pronta para impressão (layout próprio, sem menu).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Receita(int pacienteId, int id, CancellationToken ct) =>
        View(await prontuario.ObterPrescricaoImpressaoAsync(pacienteId, id, ct));

    [HttpPost, Authorize(Policy = Transacoes.ProntuarioAnexos)]
    [RequestSizeLimit(LimiteEnvio), RequestFormLimits(MultipartBodyLengthLimit = LimiteEnvio)]
    public async Task<IActionResult> Anexos(int pacienteId, AnexoFormVM vm, CancellationToken ct)
    {
        var arquivos = vm.Arquivos.Where(f => f.Length > 0).Select(f => new ArquivoEnviado(f.FileName, f.OpenReadStream())).ToList();
        try
        {
            if (await TentarAsync(() => anexos.EnviarAsync(pacienteId, new AnexoEnvioRequest(vm.Tipo, vm.Descricao, vm.EvolucaoId), arquivos, ct)))
                Sucesso(arquivos.Count == 1 ? "Arquivo anexado ao prontuário." : "{0} arquivos anexados ao prontuário.", arquivos.Count);
            else
                TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }
        finally
        {
            foreach (var a in arquivos) await a.Conteudo.DisposeAsync();
        }
        return Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#anexos");
    }

    /// <summary>Mostra (imagem/PDF) ou baixa o anexo. Cada abertura fica na auditoria.</summary>
    [HttpGet("{id:int}"), Authorize(Policy = Transacoes.ProntuarioAnexos)]
    public async Task<IActionResult> Anexo(int pacienteId, int id, bool baixar, CancellationToken ct)
    {
        var (a, conteudo) = await anexos.AbrirAsync(pacienteId, id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        if (baixar || !a.AbreNoNavegador) return File(conteudo, a.ContentType, a.NomeArquivo, enableRangeProcessing: true);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = a.NomeArquivo }.ToString();
        return File(conteudo, a.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("{id:int}"), Authorize(Policy = Transacoes.ProntuarioAnexos)]
    public async Task<IActionResult> RemoverAnexo(int pacienteId, int id, CancellationToken ct)
    {
        if (await TentarAsync(() => anexos.RemoverAsync(pacienteId, id, ct))) Sucesso("Anexo removido da lista (continua guardado para auditoria).");
        else TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return Redirect(Url.Action(nameof(Index), new { pacienteId }) + "#anexos");
    }

    private async Task CompletarAsync(EvolucaoFormVM vm, CancellationToken ct)
    {
        vm.PacienteNome = await NomePacienteAsync(vm.PacienteId, ct);
        if (vm.EvolucaoRetificadaId is int r && vm.Retificada is null)
            vm.Retificada = await prontuario.ObterEvolucaoAsync(vm.PacienteId, r, ct);
    }

    private async Task<string> NomePacienteAsync(int pacienteId, CancellationToken ct)
    {
        var p = await pacientes.ObterAsync(pacienteId, ct);
        return p.NomeSocial ?? p.Nome;
    }
}
