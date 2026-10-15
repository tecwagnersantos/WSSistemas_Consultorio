using Consultorio.Application.Admin;
using Consultorio.Domain.Entities;
using Consultorio.Web.Areas.Admin.Models;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Relatórios de todos os clientes: clientes, médicos, usuários e agendamentos (com exportação CSV).</summary>
public class RelatoriosController(AdminRelatorioService relatorios, PerfilAcessoService perfis) : AdminBaseController
{
    public IActionResult Index() => RedirectToAction(nameof(Clientes));

    public async Task<IActionResult> Clientes(string? situacao, string? acesso, string? formato, CancellationToken ct)
    {
        var vm = new ClientesVM { Situacao = situacao, Acesso = Acesso(acesso), Itens = await relatorios.ClientesAsync(SituacaoCliente(situacao), ct, Acesso(acesso)) };
        if (formato == "csv")
            return Csv($"clientes_{DateTime.Today:yyyyMMdd}.csv",
                ["Cliente", "CNPJ", "Cidade", "UF", "Situação", "Motivo da situação", "Cliente desde", "Usuários ativos", "Limite usuários", "Médicos ativos",
                 "Limite médicos", "Pacientes", "Agendamentos no mês", "Último acesso", "Versão definitiva", "Acesso até"],
                vm.Itens.Select(c => new object?[] { c.Nome, c.Cnpj, c.Cidade, c.Uf, SituacoesCliente.Nome(c.Situacao), c.MotivoSituacao, Exibicao.Data(c.CriadoEm), c.UsuariosAtivos,
                    c.MaxUsuarios, c.MedicosAtivos, c.MaxMedicos, c.Pacientes, c.AgendamentosMes,
                    c.UltimoAcessoEm is DateTime d ? Exibicao.DataHora(d) : null, c.AcessoDefinitivo, c.AcessoAte?.ToString("dd/MM/yyyy") }));
        return View(vm);
    }

    public async Task<IActionResult> Medicos(int? clinicaId, string? situacao, DateOnly? de, DateOnly? ate, string? formato, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var vm = new MedicosRelVM { ClinicaId = clinicaId, Situacao = situacao, De = de ?? new DateOnly(hoje.Year, hoje.Month, 1), Ate = ate ?? hoje };
        vm.Itens = await relatorios.MedicosAsync(clinicaId, Situacao(situacao), vm.De, vm.Ate, ct);
        if (formato == "csv")
            return Csv($"medicos_{vm.De:yyyyMMdd}_{vm.Ate:yyyyMMdd}.csv",
                ["Cliente", "Médico", "CRM", "UF", "Especialidades", "Usuário de acesso", "Ativo", "Agendamentos no período", "Atendidos no período"],
                vm.Itens.Select(m => new object?[] { m.Cliente, m.Nome, m.Crm, m.UfCrm, m.Especialidades, m.UsuarioEmail, m.Ativo, m.Agendamentos, m.Atendidos }));
        vm.Clientes = await relatorios.ClientesAsync(null, ct);
        return View(vm);
    }

    public async Task<IActionResult> Usuarios(int? clinicaId, string? perfil, string? situacao, string? formato, CancellationToken ct)
    {
        var vm = new UsuariosRelVM { ClinicaId = clinicaId, Perfil = string.IsNullOrEmpty(perfil) ? null : perfil, Situacao = situacao };
        vm.Itens = await relatorios.UsuariosAsync(clinicaId, vm.Perfil, Situacao(situacao), ct);
        if (formato == "csv")
            return Csv($"usuarios_{DateTime.Today:yyyyMMdd}.csv",
                ["Cliente", "Nome", "E-mail", "Perfis", "Ativo", "Último acesso", "Criado em"],
                vm.Itens.Select(u => new object?[] { u.Cliente, u.Nome, u.Email, string.Join(", ", u.Perfis.Select(Exibicao.NomePerfil)), u.Ativo,
                    u.UltimoAcessoEm is DateTime d ? Exibicao.DataHora(d) : null, Exibicao.Data(u.CriadoEm) }));
        vm.Clientes = await relatorios.ClientesAsync(null, ct);
        vm.Perfis = (await perfis.ListarAsync(clinicaId, ct)).Select(p => p.Nome).Distinct().Order().ToList();
        return View(vm);
    }

    public async Task<IActionResult> Agendamentos(DateOnly? de, DateOnly? ate, int? clinicaId, string? formato, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var vm = new AgendamentosRelVM { ClinicaId = clinicaId, De = de ?? new DateOnly(hoje.Year, hoje.Month, 1), Ate = ate ?? hoje };
        vm.Itens = await relatorios.AgendamentosAsync(vm.De, vm.Ate, clinicaId, ct);
        if (formato == "csv")
            return Csv($"agendamentos_{vm.De:yyyyMMdd}_{vm.Ate:yyyyMMdd}.csv",
                ["Cliente", "Total", "Em aberto", "Atendidos", "Faltas", "Taxa de faltas (%)", "Cancelados", "Remarcados"],
                vm.Itens.Select(a => new object?[] { a.Cliente, a.Total, a.EmAberto, a.Atendidos, a.Faltas, a.TaxaFaltas, a.Cancelados, a.Remarcados }));
        vm.Clientes = await relatorios.ClientesAsync(null, ct);
        return View(vm);
    }
}
