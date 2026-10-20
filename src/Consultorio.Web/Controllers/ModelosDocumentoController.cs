using Consultorio.Application.Prontuario;
using Consultorio.Domain.Common;
using Consultorio.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Controllers;

/// <summary>Modelos de texto da clínica para atestados, declarações, solicitações de exames e encaminhamentos.</summary>
[Authorize(Policy = Transacoes.ProntuarioEditar)]
public class ModelosDocumentoController(DocumentoService documentos) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await documentos.ListarModelosAsync(null, somenteAtivos: false, incluirPadrao: false, ct));

    [HttpPost]
    public async Task<IActionResult> Salvar(ModeloDocumentoFormVM vm, CancellationToken ct)
    {
        if (ModelState.IsValid && await TentarAsync(() => documentos.SalvarModeloAsync(
                new ModeloDocumentoDto(vm.Id is > 0 ? vm.Id : null, vm.Tipo, vm.Nome, vm.Texto, vm.Ativo), ct)))
            Sucesso("Modelo salvo.");
        else
            ErroNoTopo();
        return RedirectToAction(nameof(Index));
    }
}
