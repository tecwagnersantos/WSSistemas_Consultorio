using Consultorio.Application.Abstractions;
using Consultorio.Application.Admin;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Auth;

/// <summary>Regras do cadastro de demonstração (seção "Demonstracao" do appsettings).</summary>
public record DemonstracaoConfig(bool Habilitado = true, short Dias = 15, short MaxMedicos = 2, short MaxUsuarios = 3);

public record DemonstracaoRequest(string NomeClinica, string? Telefone, string? Cidade, string? Uf,
    string NomeResponsavel, string Email, string Senha, bool SouMedico, string? Crm, string? UfCrm);

/// <summary>
/// Cadastro feito pelo próprio interessado na tela de login: cria a clínica em período de teste
/// (AcessoAte = hoje + N dias) e o usuário administrador dela (também médico, se informar o CRM).
/// Quando o prazo acaba o login é recusado; para continuar, o administrador do sistema libera a versão
/// definitiva ou estende o prazo em /Admin → Clientes.
/// </summary>
public class DemonstracaoService(IAppDbContext db, ClienteService clientes, IAuditoria auditoria)
{
    public const string MarcaObservacao = "Demonstração cadastrada pelo site";

    public async Task<int> CadastrarAsync(DemonstracaoRequest req, DemonstracaoConfig config, CancellationToken ct)
    {
        if (!config.Habilitado) throw new DomainException("O cadastro de demonstração não está disponível.");
        if (string.IsNullOrWhiteSpace(req.NomeClinica) || string.IsNullOrWhiteSpace(req.NomeResponsavel) || string.IsNullOrWhiteSpace(req.Email))
            throw new DomainException("Informe o nome da clínica, o seu nome e o e-mail.");

        string? crm = null, ufCrm = null;
        if (req.SouMedico)
        {
            crm = new string((req.Crm ?? "").Where(char.IsDigit).ToArray());
            ufCrm = req.UfCrm?.Trim().ToUpperInvariant();
            if (crm.Length is < 4 or > 10 || ufCrm is not { Length: 2 })
                throw new DomainException("Informe o CRM (somente números) e a UF do CRM.");
        }

        // Perfis padrão (globais): o responsável administra a clínica e, se for médico, também atende
        var nomesPerfis = req.SouMedico ? new[] { Perfis.Administrador, Perfis.Medico } : new[] { Perfis.Administrador };
        var perfilIds = await db.Perfis.AsNoTracking().Where(p => p.ClinicaId == null && p.Ativo && nomesPerfis.Contains(p.Nome))
            .Select(p => p.Id).ToListAsync(ct);
        if (perfilIds.Count != nomesPerfis.Length) throw new DomainException("Perfis de acesso padrão não encontrados. Contate o suporte.");

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var cliente = new ClienteSalvarRequest(req.NomeClinica.Trim(), null, req.Telefone, req.Email.Trim(), null,
            req.Cidade, req.Uf, config.MaxMedicos, config.MaxUsuarios,
            $"{MarcaObservacao} em {DateTime.Now:dd/MM/yyyy HH:mm}. Responsável: {req.NomeResponsavel.Trim()}.",
            SituacoesCliente.Ativo, AcessoDefinitivo: false, AcessoAte: hoje.AddDays(config.Dias));
        var admin = new UsuarioClienteRequest(req.NomeResponsavel, req.Email, req.Senha, perfilIds);
        var clinicaId = await clientes.SalvarAsync(null, cliente, admin, ct);

        if (req.SouMedico)
        {
            var usuarioId = await db.Usuarios.IgnoreQueryFilters().Where(u => u.ClinicaId == clinicaId)
                .Select(u => u.Id).FirstAsync(ct);
            var medico = new Medico
            {
                ClinicaId = clinicaId, UsuarioId = usuarioId, Nome = req.NomeResponsavel.Trim(), Crm = crm!, UfCrm = ufCrm!,
                Email = req.Email.Trim().ToLowerInvariant(), Telefone = req.Telefone
            };
            db.Medicos.Add(medico);
            await db.SaveChangesAsync(ct);
            auditoria.Registrar("INCLUSAO", "cadastro.Medico", medico.Id, novo: new { medico.Nome, medico.Crm, medico.UfCrm }, clinicaId: clinicaId);
            await db.SaveChangesAsync(ct);
        }
        return clinicaId;
    }
}
