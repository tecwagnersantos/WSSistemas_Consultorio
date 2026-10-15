using Consultorio.Application.Admin;
using Consultorio.Domain.Entities;
using Consultorio.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace Consultorio.Web.Areas.Admin.Controllers;

/// <summary>Manutenção dos clientes (clínicas): dados, limites do plano, situação (ativo/bloqueado/inativo) e usuários.</summary>
public class ClientesController(ClienteService clientes, AdminRelatorioService relatorios, PerfilAcessoService perfis, CobrancaService cobrancas) : AdminBaseController
{
    public async Task<IActionResult> Index(string? situacao, string? acesso, CancellationToken ct) =>
        View(new ClientesVM { Situacao = situacao, Acesso = Acesso(acesso), Itens = await relatorios.ClientesAsync(SituacaoCliente(situacao), ct, Acesso(acesso)) });

    public async Task<IActionResult> Detalhes(int id, CancellationToken ct)
    {
        ViewBag.Perfis = await perfis.OpcoesAsync(id, ct);
        ViewBag.Cobrancas = await cobrancas.ListarAsync(id, null, null, null, ct);
        return View(await clientes.DetalhesAsync(id, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Form(int? id, CancellationToken ct)
    {
        if (id is not int cid)
        {
            var novo = new ClienteFormVM { PerfisPadrao = await perfis.OpcoesPadraoAsync(ct) };
            novo.AdminPerfilId = novo.PerfisPadrao.FirstOrDefault(p => p.Nome == Domain.Common.Perfis.Administrador)?.Id;
            return View(novo);
        }
        var c = await clientes.ObterAsync(cid, ct);
        return View(new ClienteFormVM
        {
            Id = c.Id, Nome = c.Nome, Cnpj = c.Cnpj, Telefone = c.Telefone, Email = c.Email, Endereco = c.Endereco,
            Cidade = c.Cidade, Uf = c.Uf, MaxMedicos = c.MaxMedicos, MaxUsuarios = c.MaxUsuarios, Observacoes = c.Observacoes,
            Situacao = c.Situacao, MotivoSituacao = c.MotivoSituacao,
            ValorMensalidade = c.ValorMensalidade, DiaVencimento = c.DiaVencimento, DiasTolerancia = c.DiasTolerancia, BloqueioPorAtraso = c.BloqueioPorAtraso,
            AcessoDefinitivo = c.AcessoDefinitivo, AcessoAte = c.AcessoAte
        });
    }

    [HttpPost]
    public async Task<IActionResult> Form(ClienteFormVM vm, CancellationToken ct)
    {
        UsuarioClienteRequest? admin = null;
        if (vm.Id is null)
        {
            if (string.IsNullOrWhiteSpace(vm.AdminNome) || string.IsNullOrWhiteSpace(vm.AdminEmail) || string.IsNullOrWhiteSpace(vm.AdminSenha))
                ModelState.AddModelError("", T["Informe nome, e-mail e senha do administrador da clínica."]);
            else
                admin = new UsuarioClienteRequest(vm.AdminNome, vm.AdminEmail, vm.AdminSenha, vm.AdminPerfilId is int pid ? [pid] : []);
        }
        var req = new ClienteSalvarRequest(vm.Nome, vm.Cnpj, vm.Telefone, vm.Email, vm.Endereco, vm.Cidade, vm.Uf,
            vm.MaxMedicos, vm.MaxUsuarios, vm.Observacoes, vm.Situacao, vm.AcessoDefinitivo, vm.AcessoAte, vm.MotivoSituacao,
            vm.ValorMensalidade, vm.DiaVencimento, vm.DiasTolerancia, vm.BloqueioPorAtraso);
        int id = 0;
        if (ModelState.IsValid && await TentarAsync(async () => id = await clientes.SalvarAsync(vm.Id, req, admin, ct)))
        {
            Sucesso(vm.Id is null ? "Cliente criado. O administrador da clínica já pode entrar com o e-mail e a senha informados." : "Cliente salvo.");
            return RedirectToAction(nameof(Detalhes), new { id });
        }
        vm.AdminSenha = null;
        if (vm.Id is null) vm.PerfisPadrao = await perfis.OpcoesPadraoAsync(ct);
        return View(vm);
    }

    /// <summary>Bloquear, inativar ou reativar o cliente (botões da tela de detalhes).</summary>
    [HttpPost]
    public async Task<IActionResult> AlterarSituacao(int id, string situacao, string? motivo, CancellationToken ct)
    {
        if (await TentarAsync(() => clientes.AlterarSituacaoAsync(id, situacao, motivo, ct)))
            Sucesso(situacao switch
            {
                SituacoesCliente.Bloqueado => "Cliente bloqueado: os usuários da clínica não entram mais e as sessões abertas foram encerradas.",
                SituacoesCliente.Inativo => "Cliente inativado: não entra mais no sistema e saiu da lista de clientes atuais. Os dados continuam guardados.",
                _ => "Cliente reativado: os usuários da clínica já podem entrar."
            });
        else
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> LiberarDefinitivo(int id, CancellationToken ct)
    {
        await clientes.LiberarDefinitivoAsync(id, ct);
        Sucesso("Versão definitiva liberada: o acesso desta clínica não tem mais data para terminar.");
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> NovoUsuario(int id, NovoUsuarioClienteVM vm, CancellationToken ct)
    {
        if (ModelState.IsValid && await TentarAsync(() => clientes.NovoUsuarioAsync(id, new UsuarioClienteRequest(vm.Nome, vm.Email, vm.Senha, vm.PerfilIds), ct)))
            Sucesso("Usuário criado.");
        else
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SituacaoUsuario(int id, int usuarioId, bool ativo, CancellationToken ct)
    {
        await clientes.AlterarSituacaoUsuarioAsync(id, usuarioId, ativo, ct);
        Sucesso(ativo ? "Usuário reativado." : "Usuário inativado.");
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    /// <summary>Perfis de acesso de um usuário da clínica.</summary>
    [HttpPost]
    public async Task<IActionResult> PerfisUsuario(int id, int usuarioId, List<int> perfilIds, CancellationToken ct)
    {
        if (await TentarAsync(() => clientes.AlterarPerfisUsuarioAsync(id, usuarioId, perfilIds, ct)))
            Sucesso("Perfis do usuário atualizados. Valem a partir da próxima página que ele abrir.");
        else
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RedefinirSenha(int id, int usuarioId, string senha, CancellationToken ct)
    {
        await clientes.RedefinirSenhaUsuarioAsync(id, usuarioId, senha ?? "", ct);
        Sucesso("Senha redefinida e usuário desbloqueado. Passe a nova senha ao usuário.");
        return RedirectToAction(nameof(Detalhes), new { id });
    }
}
