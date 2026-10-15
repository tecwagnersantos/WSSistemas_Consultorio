using System.Globalization;
using Consultorio.Application.Admin;
using Consultorio.Domain.Entities;
using Consultorio.Web.Areas.Admin.Models;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Controle de pagamentos dos clientes: mensalidades e cobranças avulsas, recebimentos, estornos e prorrogações.</summary>
public class PagamentosController(CobrancaService cobrancas, AdminRelatorioService relatorios) : AdminBaseController
{
    public async Task<IActionResult> Index(int? clinicaId, string? status, DateOnly? de, DateOnly? ate, string? formato, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var iniMes = new DateOnly(hoje.Year, hoje.Month, 1);
        // Sem filtro escolhido abre nas pendentes (em aberto e atrasadas), que é o que o suporte acompanha no dia a dia
        status = status switch { null => "PENDENTES", "TODAS" => null, _ => status };
        var vm = new PagamentosVM
        {
            ClinicaId = clinicaId, Status = status ?? "TODAS", De = de, Ate = ate,
            Itens = await cobrancas.ListarAsync(clinicaId, status, de, ate, ct),
            ResumoMes = await cobrancas.ResumoAsync(iniMes, iniMes.AddMonths(1).AddDays(-1), ct),
            Clientes = await relatorios.ClientesAsync(null, ct)
        };
        if (formato == "csv")
            return Csv($"pagamentos_{DateTime.Today:yyyyMMdd}.csv",
                ["Cliente", "Descrição", "Competência", "Vencimento", "Valor", "Situação", "Pago em", "Valor pago", "Forma", "Observações"],
                vm.Itens.Select(c => new object?[] { c.Cliente, c.Descricao, c.Competencia?.ToString("MM/yyyy"), c.Vencimento.ToString("dd/MM/yyyy"),
                    c.Valor, StatusCobranca.Nome(c.Status), c.PagoEm?.ToString("dd/MM/yyyy"), c.ValorPago, FormasPagamento.Nome(c.FormaPagamento), c.Observacoes }));
        return View(vm);
    }

    /// <summary>Gera as mensalidades de um mês para todos os clientes com valor de mensalidade (não repete as já geradas).</summary>
    [HttpPost]
    public async Task<IActionResult> Gerar(string? competencia, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(competencia + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var comp))
        {
            TempData["Erro"] = T["Informe o mês das mensalidades."].Value;
            return RedirectToAction(nameof(Index));
        }
        var n = await cobrancas.GerarMensalidadesAsync(comp, automatico: false, ct);
        Sucesso(n == 0 ? "Nenhuma mensalidade nova para {1:MM/yyyy}: todos os clientes com mensalidade já têm a cobrança do mês."
                       : "{0} mensalidade(s) de {1:MM/yyyy} gerada(s).", n, comp);
        return RedirectToAction(nameof(Index), new { status = "TODAS", de = Exibicao.Iso(comp), ate = Exibicao.Iso(comp.AddMonths(1).AddDays(-1)) });
    }

    [HttpPost]
    public async Task<IActionResult> Nova(int clinicaId, NovaCobrancaVM vm, string? voltar, CancellationToken ct)
    {
        if (await TentarAsync(() => cobrancas.CriarAsync(clinicaId,
                new NovaCobrancaRequest(vm.Descricao ?? "", vm.Vencimento ?? default, vm.Valor ?? 0, vm.Mensalidade ? vm.Vencimento : null), ct)))
            Sucesso("Cobrança criada.");
        else Erros();
        return Voltar(voltar);
    }

    [HttpGet]
    public async Task<IActionResult> Pagar(int id, string? voltar, CancellationToken ct)
    {
        var c = await cobrancas.ObterAsync(id, ct);
        return View(new PagamentoVM { Cobranca = c, ValorPago = c.Valor, Voltar = voltar });
    }

    [HttpPost]
    public async Task<IActionResult> Pagar(int id, PagamentoVM vm, CancellationToken ct)
    {
        if (await TentarAsync(() => cobrancas.RegistrarPagamentoAsync(id,
                new PagamentoRequest(vm.PagoEm, vm.ValorPago ?? 0, vm.FormaPagamento ?? "", vm.Observacoes), ct)))
        {
            Sucesso("Pagamento registrado.");
            return Voltar(vm.Voltar);
        }
        vm.Cobranca = await cobrancas.ObterAsync(id, ct);
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Estornar(int id, string? motivo, string? voltar, CancellationToken ct)
    {
        if (await TentarAsync(() => cobrancas.EstornarPagamentoAsync(id, motivo, ct))) Sucesso("Pagamento estornado: a cobrança voltou a ficar em aberto.");
        else Erros();
        return Voltar(voltar);
    }

    [HttpPost]
    public async Task<IActionResult> Cancelar(int id, string? motivo, string? voltar, CancellationToken ct)
    {
        if (await TentarAsync(() => cobrancas.CancelarAsync(id, motivo, ct))) Sucesso("Cobrança cancelada.");
        else Erros();
        return Voltar(voltar);
    }

    [HttpPost]
    public async Task<IActionResult> Prorrogar(int id, DateOnly? vencimento, string? voltar, CancellationToken ct)
    {
        if (vencimento is null) ModelState.AddModelError("", T["Informe o novo vencimento."]);
        else if (await TentarAsync(() => cobrancas.ProrrogarAsync(id, vencimento.Value, ct)))
        {
            Sucesso("Vencimento alterado para {0:d}.", vencimento);
            return Voltar(voltar);
        }
        Erros();
        return Voltar(voltar);
    }

    private void Erros() => TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

    private IActionResult Voltar(string? voltar) =>
        !string.IsNullOrEmpty(voltar) && Url.IsLocalUrl(voltar) ? LocalRedirect(voltar) : RedirectToAction(nameof(Index));
}
