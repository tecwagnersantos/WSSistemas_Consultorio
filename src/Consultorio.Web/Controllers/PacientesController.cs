using Consultorio.Application.Cadastros;
using Consultorio.Application.Pacientes;
using Consultorio.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

[Authorize(Policy = Transacoes.PacientesVer)]
public class PacientesController(PacienteService pacientes, CadastroService cadastros) : BaseController
{
    public async Task<IActionResult> Index(string? termo, int pagina = 1, CancellationToken ct = default)
    {
        ViewBag.Termo = termo;
        return View(await pacientes.PesquisarAsync(termo, pagina, 20, ct));
    }

    [HttpGet, Authorize(Policy = Transacoes.PacientesEditar)]
    public async Task<IActionResult> Novo(CancellationToken ct)
    {
        await CarregarConveniosAsync(ct);
        return View("Form", new PacienteDto { DataNascimento = default });
    }

    [HttpPost, Authorize(Policy = Transacoes.PacientesEditar)]
    public async Task<IActionResult> Novo(PacienteDto dto, string? depois, CancellationToken ct)
    {
        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () => id = await pacientes.SalvarAsync(null, dto, ct)))
        {
            Sucesso("Paciente cadastrado.");
            if (depois == "agendar")
                return RedirectToAction("Novo", "Agenda", new { pacienteId = id, pacienteNome = dto.NomeSocial ?? dto.Nome });
            return RedirectToAction(nameof(Editar), new { id });
        }
        await CarregarConveniosAsync(ct);
        return View("Form", dto);
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        await CarregarConveniosAsync(ct);
        return View("Form", await pacientes.ObterAsync(id, ct));
    }

    [HttpPost, Authorize(Policy = Transacoes.PacientesEditar)]
    public async Task<IActionResult> Editar(int id, PacienteDto dto, CancellationToken ct)
    {
        dto.Id = id;
        if (ModelState.IsValid && await TentarAsync(() => pacientes.SalvarAsync(id, dto, ct)))
        {
            Sucesso("Cadastro atualizado.");
            return RedirectToAction(nameof(Editar), new { id });
        }
        await CarregarConveniosAsync(ct);
        return View("Form", dto);
    }

    /// <summary>JSON: busca rápida para o campo de paciente da tela de agendamento.</summary>
    [HttpGet]
    public async Task<IActionResult> Buscar(string? termo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(termo) || termo.Trim().Length < 2) return Json(Array.Empty<object>());
        var r = await pacientes.PesquisarAsync(termo, 1, 10, ct);
        return Json(r.Itens.Where(p => p.Ativo).Select(p => new
        {
            id = p.Id,
            nome = p.NomeSocial ?? p.Nome,
            cpf = p.Cpf,
            nascimento = p.DataNascimento.ToString("dd/MM/yyyy"),
            celular = p.Celular
        }));
    }

    /// <summary>JSON: carteirinhas ativas do paciente.</summary>
    [HttpGet]
    public async Task<IActionResult> Carteirinhas(int id, CancellationToken ct)
    {
        var lista = await pacientes.CarteirinhasAsync(id, ct);
        return Json(lista.Select(c => new { id = c.Id, descricao = $"{c.Convenio} - {c.Plano} ({c.NumeroCarteirinha})" }));
    }

    private async Task CarregarConveniosAsync(CancellationToken ct) =>
        ViewBag.Convenios = await cadastros.ListarConveniosAsync(ct);
}
