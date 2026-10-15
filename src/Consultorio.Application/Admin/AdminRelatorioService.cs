using Consultorio.Application.Abstractions;
using Consultorio.Application.Relatorios;
using Consultorio.Domain.Entities;
using Consultorio.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Admin;

public record ClienteResumoDto(int Id, string Nome, string? Cnpj, string? Cidade, string? Uf, string Situacao, DateTime CriadoEm,
    int UsuariosAtivos, short? MaxUsuarios, int MedicosAtivos, short? MaxMedicos, int Pacientes, int AgendamentosMes,
    DateTime? UltimoAcessoEm, bool AcessoDefinitivo, DateOnly? AcessoAte, string? MotivoSituacao = null, DateTime? SituacaoDesde = null)
{
    public bool Ativo => Situacao == SituacoesCliente.Ativo;

    /// <summary>Dias de acesso que restam no período de teste (negativo = vencido; null = definitivo).</summary>
    public int? DiasDeAcesso => AcessoDefinitivo ? null : (AcessoAte?.DayNumber ?? 0) - DateOnly.FromDateTime(DateTime.Today).DayNumber;
    public bool AcessoVencido => DiasDeAcesso < 0;
    /// <summary>Período de teste termina nos próximos 15 dias.</summary>
    public bool AcessoVencendo => DiasDeAcesso is >= 0 and <= 15;

    /// <summary>Usuários ou médicos ativos em 90% ou mais do limite do plano.</summary>
    public bool PertoDoLimite => Perto(UsuariosAtivos, MaxUsuarios) || Perto(MedicosAtivos, MaxMedicos);
    public bool AcimaDoLimite => UsuariosAtivos > MaxUsuarios || MedicosAtivos > MaxMedicos;
    private static bool Perto(int usados, short? max) => max is short m && usados >= Math.Ceiling(m * 0.9);
}

public record AdminDashboardDto(int ClientesAtivos, int ClientesBloqueados, int ClientesInativos, int ClientesEmTeste, int UsuariosAtivos, int MedicosAtivos, int Pacientes,
    int AgendamentosMes, int AtendidosMes, int AcessosUltimos7Dias, IReadOnlyList<SerieDiaDto> AgendamentosUltimos30Dias,
    IReadOnlyList<ClienteResumoDto> MaisAgendamentosMes, IReadOnlyList<ClienteResumoDto> PertoDoLimite,
    IReadOnlyList<ClienteResumoDto> AcessoVencendo, IReadOnlyList<ClienteResumoDto> Bloqueados);

public record MedicoRelDto(string Cliente, int Id, string Nome, string Crm, string UfCrm, string Especialidades,
    string? UsuarioEmail, bool Ativo, int Agendamentos, int Atendidos);

public record UsuarioRelDto(string Cliente, int Id, string Nome, string Email, IReadOnlyList<string> Perfis, bool Ativo,
    DateTime? UltimoAcessoEm, DateTime CriadoEm);

public record AgendamentoRelDto(int ClinicaId, string Cliente, int Total, int EmAberto, int Atendidos, int Faltas,
    int Cancelados, int Remarcados)
{
    public decimal TaxaFaltas => Atendidos + Faltas == 0 ? 0 : Math.Round(Faltas * 100m / (Atendidos + Faltas), 1);
}

/// <summary>
/// Dashboard e relatórios da área /Admin, somando todos os clientes. Usa IgnoreQueryFilters() de propósito
/// (o administrador do sistema não tem clínica). Só contagens: não expõe dados de pacientes nem do prontuário.
/// </summary>
public class AdminRelatorioService(IAppDbContext db)
{
    private static readonly StatusAgendamento[] EmAberto =
        [StatusAgendamento.Agendado, StatusAgendamento.Confirmado, StatusAgendamento.Aguardando, StatusAgendamento.EmAtendimento];

    public async Task<AdminDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var hoje = DateTime.Today;
        var iniMes = new DateTime(hoje.Year, hoje.Month, 1);
        var fimMes = iniMes.AddMonths(1);
        var ini30 = hoje.AddDays(-29);

        var clientes = await ClientesAsync(null, ct);
        var ativos = clientes.Where(c => c.Ativo).ToList();
        var ag = db.Agendamentos.IgnoreQueryFilters();
        var agMes = await ag.CountAsync(a => a.Inicio >= iniMes && a.Inicio < fimMes, ct);
        var atendidosMes = await ag.CountAsync(a => a.Inicio >= iniMes && a.Inicio < fimMes && a.StatusId == StatusAgendamento.Atendido, ct);
        var acessos = await db.LogsAuditoria.CountAsync(l => l.Acao == "LOGIN" && l.ClinicaId != null && l.OcorridoEm >= hoje.AddDays(-6), ct);
        var serie = await ag.Where(a => a.Inicio >= ini30 && a.Inicio < hoje.AddDays(1))
            .GroupBy(a => a.Inicio.Date)
            .Select(g => new SerieDiaDto(g.Key,
                g.Count(a => a.StatusId == StatusAgendamento.Atendido),
                g.Count(a => a.StatusId == StatusAgendamento.Faltou),
                g.Count(a => a.StatusId == StatusAgendamento.Cancelado)))
            .ToListAsync(ct);

        return new AdminDashboardDto(
            clientes.Count(c => c.Ativo), clientes.Count(c => c.Situacao == SituacoesCliente.Bloqueado),
            clientes.Count(c => c.Situacao == SituacoesCliente.Inativo), clientes.Count(c => c.Ativo && !c.AcessoDefinitivo),
            // Totais de uso só dos clientes ativos (bloqueados e inativos não estão usando o sistema)
            ativos.Sum(c => c.UsuariosAtivos), ativos.Sum(c => c.MedicosAtivos), ativos.Sum(c => c.Pacientes),
            agMes, atendidosMes, acessos, serie.OrderBy(s => s.Data).ToList(),
            clientes.Where(c => c.AgendamentosMes > 0).OrderByDescending(c => c.AgendamentosMes).Take(8).ToList(),
            clientes.Where(c => c.Ativo && (c.PertoDoLimite || c.AcimaDoLimite)).ToList(),
            clientes.Where(c => c.Ativo && (c.AcessoVencido || c.AcessoVencendo)).OrderBy(c => c.AcessoAte).ToList(),
            clientes.Where(c => c.Situacao == SituacoesCliente.Bloqueado).OrderBy(c => c.SituacaoDesde).ToList());
    }

    /// <summary>
    /// Clientes com uso do plano. <paramref name="situacao"/>: ATIVO, BLOQUEADO, INATIVO, <see cref="SituacaoAtuais"/>
    /// (ativos e bloqueados, sem os ex-clientes) ou null = todos.
    /// <paramref name="acesso"/>: "teste", "definitivo", "vencidos" (teste já encerrado) ou null = todos.
    /// </summary>
    public async Task<List<ClienteResumoDto>> ClientesAsync(string? situacao, CancellationToken ct, string? acesso = null)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var iniMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var fimMes = iniMes.AddMonths(1);
        return await db.Clinicas.AsNoTracking()
            .Where(c => situacao == null || c.Situacao == situacao
                        || (situacao == SituacaoAtuais && c.Situacao != SituacoesCliente.Inativo))
            .Where(c => acesso == null
                        || (acesso == "definitivo" && c.AcessoDefinitivo)
                        || (acesso == "teste" && !c.AcessoDefinitivo)
                        || (acesso == "vencidos" && !c.AcessoDefinitivo && (c.AcessoAte == null || c.AcessoAte < hoje)))
            .OrderBy(c => c.Nome)
            .Select(c => new ClienteResumoDto(c.Id, c.Nome, c.Cnpj, c.Cidade, c.Uf, c.Situacao, c.CriadoEm,
                db.Usuarios.IgnoreQueryFilters().Count(u => u.ClinicaId == c.Id && u.Ativo), c.MaxUsuarios,
                db.Medicos.IgnoreQueryFilters().Count(m => m.ClinicaId == c.Id && m.Ativo), c.MaxMedicos,
                db.Pacientes.IgnoreQueryFilters().Count(p => p.ClinicaId == c.Id && p.Ativo),
                db.Agendamentos.IgnoreQueryFilters().Count(a => a.ClinicaId == c.Id && a.Inicio >= iniMes && a.Inicio < fimMes),
                db.Usuarios.IgnoreQueryFilters().Where(u => u.ClinicaId == c.Id).Max(u => u.UltimoAcessoEm),
                c.AcessoDefinitivo, c.AcessoAte, c.MotivoSituacao, c.SituacaoDesde))
            .ToListAsync(ct);
    }

    /// <summary>Filtro "clientes atuais": ativos e bloqueados (exclui os inativos).</summary>
    public const string SituacaoAtuais = "ATUAIS";

    public async Task<List<MedicoRelDto>> MedicosAsync(int? clinicaId, bool? ativos, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var lista = await (from m in db.Medicos.IgnoreQueryFilters().AsNoTracking()
                           join c in db.Clinicas on m.ClinicaId equals c.Id
                           where (clinicaId == null || m.ClinicaId == clinicaId) && (ativos == null || m.Ativo == ativos)
                           orderby c.Nome, m.Nome
                           select new
                           {
                               Cliente = c.Nome, m.Id, m.Nome, m.Crm, m.UfCrm, m.Ativo,
                               Especialidades = m.Especialidades.Select(e => e.Especialidade.Nome).ToList(),
                               UsuarioEmail = db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == m.UsuarioId).Select(u => u.Email).FirstOrDefault(),
                               Agendamentos = db.Agendamentos.IgnoreQueryFilters().Count(a => a.MedicoId == m.Id && a.Inicio >= ini && a.Inicio < fim),
                               Atendidos = db.Agendamentos.IgnoreQueryFilters().Count(a => a.MedicoId == m.Id && a.Inicio >= ini && a.Inicio < fim
                                                                                          && a.StatusId == StatusAgendamento.Atendido)
                           }).IgnoreQueryFilters().ToListAsync(ct);
        return lista.Select(m => new MedicoRelDto(m.Cliente, m.Id, m.Nome, m.Crm, m.UfCrm, string.Join(", ", m.Especialidades),
            m.UsuarioEmail, m.Ativo, m.Agendamentos, m.Atendidos)).ToList();
    }

    public async Task<List<UsuarioRelDto>> UsuariosAsync(int? clinicaId, string? perfil, bool? ativos, CancellationToken ct) =>
        await (from u in db.Usuarios.IgnoreQueryFilters().AsNoTracking()
               join c in db.Clinicas on u.ClinicaId equals c.Id
               where (clinicaId == null || u.ClinicaId == clinicaId) && (ativos == null || u.Ativo == ativos)
                     && (perfil == null || u.Perfis.Any(p => p.Perfil.Nome == perfil))
               orderby c.Nome, u.Nome
               select new UsuarioRelDto(c.Nome, u.Id, u.Nome, u.Email, u.Perfis.Select(p => p.Perfil.Nome).ToList(), u.Ativo,
                   u.UltimoAcessoEm, u.CriadoEm))
            .IgnoreQueryFilters().ToListAsync(ct);

    public async Task<List<AgendamentoRelDto>> AgendamentosAsync(DateOnly de, DateOnly ate, int? clinicaId, CancellationToken ct)
    {
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var porClinica = await db.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Inicio >= ini && a.Inicio < fim && (clinicaId == null || a.ClinicaId == clinicaId))
            .GroupBy(a => a.ClinicaId)
            .Select(g => new
            {
                ClinicaId = g.Key,
                Total = g.Count(),
                EmAberto = g.Count(a => EmAberto.Contains(a.StatusId)),
                Atendidos = g.Count(a => a.StatusId == StatusAgendamento.Atendido),
                Faltas = g.Count(a => a.StatusId == StatusAgendamento.Faltou),
                Cancelados = g.Count(a => a.StatusId == StatusAgendamento.Cancelado),
                Remarcados = g.Count(a => a.StatusId == StatusAgendamento.Remarcado)
            }).ToListAsync(ct);
        var nomes = await db.Clinicas.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        return porClinica
            .Select(x => new AgendamentoRelDto(x.ClinicaId, nomes.GetValueOrDefault(x.ClinicaId, "?"), x.Total, x.EmAberto,
                x.Atendidos, x.Faltas, x.Cancelados, x.Remarcados))
            .OrderByDescending(x => x.Total).ToList();
    }
}
