using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Consultorio.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Agenda;

public record AgendamentoDto(int Id, DateTime Inicio, DateTime Fim, StatusAgendamento Status, TipoAtendimento Tipo,
    int MedicoId, string Medico, int EspecialidadeId, string Especialidade, int PacienteId, string Paciente,
    string? Celular, int? PacientePlanoId, string? Convenio, decimal? Valor, string? Observacoes);

public record NovoAgendamentoRequest(int PacienteId, int MedicoId, int EspecialidadeId, int? PacientePlanoId,
    TipoAtendimento Tipo, DateTime Inicio, int? DuracaoMin, decimal? Valor, string? Observacoes);

public record AlterarStatusRequest(StatusAgendamento Status, string? Observacao);
public record RemarcarRequest(DateTime NovoInicio, int? DuracaoMin, string? Observacao);
public record HorarioLivreDto(DateTime Inicio, DateTime Fim);
public record BloqueioDto(int Id, int? MedicoId, DateTime Inicio, DateTime Fim, string? Motivo);

public class AgendaService(IAppDbContext db, IUsuarioAtual usuario)
{
    private static readonly StatusAgendamento[] Ocupantes =
        Enum.GetValues<StatusAgendamento>().Where(s => s.OcupaHorario()).ToArray();

    public async Task<List<AgendamentoDto>> ListarAsync(DateOnly de, DateOnly ate, int? medicoId, CancellationToken ct)
    {
        medicoId = RestringirAoMedicoLogado(medicoId);
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        return await db.Agendamentos.AsNoTracking()
            .Where(a => a.Inicio >= ini && a.Inicio < fim && (medicoId == null || a.MedicoId == medicoId))
            .OrderBy(a => a.Inicio)
            .Select(a => new AgendamentoDto(a.Id, a.Inicio, a.Fim, a.StatusId, a.TipoAtendimentoId,
                a.MedicoId, a.Medico.Nome, a.EspecialidadeId, a.Especialidade.Nome,
                a.PacienteId, a.Paciente.NomeSocial ?? a.Paciente.Nome, a.Paciente.Celular,
                a.PacientePlanoId, a.PacientePlano != null ? a.PacientePlano.Plano.Convenio.Nome : null,
                a.Valor, a.Observacoes))
            .ToListAsync(ct);
    }

    /// <summary>Gera os horários livres do médico no dia, a partir da grade, descontando bloqueios e agendamentos.</summary>
    public async Task<List<HorarioLivreDto>> HorariosLivresAsync(int medicoId, DateOnly dia, CancellationToken ct)
    {
        var diaSemana = (byte)dia.DayOfWeek;
        var grades = await db.HorariosAtendimento.AsNoTracking()
            .Where(h => h.MedicoId == medicoId && h.Ativo && h.DiaSemana == diaSemana
                        && h.VigenciaInicio <= dia && (h.VigenciaFim == null || h.VigenciaFim >= dia))
            .ToListAsync(ct);

        var ini = dia.ToDateTime(TimeOnly.MinValue);
        var fim = ini.AddDays(1);
        var ocupados = await db.Agendamentos.AsNoTracking()
            .Where(a => a.MedicoId == medicoId && a.Inicio < fim && a.Fim > ini && Ocupantes.Contains(a.StatusId))
            .Select(a => new { a.Inicio, a.Fim }).ToListAsync(ct);
        var bloqueios = await db.BloqueiosAgenda.AsNoTracking()
            .Where(b => (b.MedicoId == null || b.MedicoId == medicoId) && b.Inicio < fim && b.Fim > ini)
            .Select(b => new { b.Inicio, b.Fim }).ToListAsync(ct);

        var livres = new List<HorarioLivreDto>();
        foreach (var g in grades)
        {
            for (var t = dia.ToDateTime(g.HoraInicio); t.AddMinutes(g.DuracaoSlotMin) <= dia.ToDateTime(g.HoraFim); t = t.AddMinutes(g.DuracaoSlotMin))
            {
                var tFim = t.AddMinutes(g.DuracaoSlotMin);
                if (t < DateTime.Now) continue;
                if (ocupados.Any(o => o.Inicio < tFim && o.Fim > t) || bloqueios.Any(b => b.Inicio < tFim && b.Fim > t)) continue;
                livres.Add(new(t, tFim));
            }
        }
        return livres.OrderBy(l => l.Inicio).ToList();
    }

    public async Task<int> AgendarAsync(NovoAgendamentoRequest req, CancellationToken ct)
    {
        var medico = await db.Medicos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.MedicoId && m.Ativo, ct)
            ?? throw new DomainException("Médico não encontrado ou inativo.");
        if (!await db.Pacientes.AnyAsync(p => p.Id == req.PacienteId && p.Ativo, ct))
            throw new DomainException("Paciente não encontrado ou inativo.");
        if (!await db.Especialidades.AnyAsync(e => e.Id == req.EspecialidadeId, ct))
            throw new DomainException("Especialidade não encontrada.");
        if (req.PacientePlanoId is int pp && !await db.PacientePlanos.AnyAsync(x => x.Id == pp && x.PacienteId == req.PacienteId && x.Ativo, ct))
            throw new DomainException("Carteirinha não pertence ao paciente.");

        var fim = req.Inicio.AddMinutes(req.DuracaoMin ?? medico.DuracaoConsultaPadraoMin);
        var ag = new Agendamento
        {
            PacienteId = req.PacienteId, MedicoId = req.MedicoId, EspecialidadeId = req.EspecialidadeId,
            PacientePlanoId = req.PacientePlanoId, TipoAtendimentoId = req.Tipo, Inicio = req.Inicio, Fim = fim,
            Valor = req.Valor, Observacoes = req.Observacoes, CriadoPorUsuarioId = usuario.UsuarioId
        };
        ag.Historico.Add(new AgendamentoHistoricoStatus { StatusNovoId = StatusAgendamento.Agendado, UsuarioId = usuario.UsuarioId });

        return await db.ExecutarComLockAsync($"agenda-medico-{req.MedicoId}", async () =>
        {
            await ValidarConflitoAsync(req.MedicoId, req.Inicio, fim, null, ct);
            db.Agendamentos.Add(ag);
            await db.SaveChangesAsync(ct);
            return ag.Id;
        }, ct);
    }

    public async Task AlterarStatusAsync(int id, AlterarStatusRequest req, CancellationToken ct)
    {
        var ag = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NaoEncontradoException("Agendamento não encontrado.");
        if (req.Status == StatusAgendamento.Remarcado)
            throw new DomainException("Use a operação de remarcação.");
        ag.AlterarStatus(req.Status, usuario.UsuarioId, req.Observacao);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marca o atual como REMARCADO e cria um novo apontando para ele.</summary>
    public async Task<int> RemarcarAsync(int id, RemarcarRequest req, CancellationToken ct)
    {
        var atual = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NaoEncontradoException("Agendamento não encontrado.");
        var duracao = req.DuracaoMin ?? (int)(atual.Fim - atual.Inicio).TotalMinutes;
        var fim = req.NovoInicio.AddMinutes(duracao);

        return await db.ExecutarComLockAsync($"agenda-medico-{atual.MedicoId}", async () =>
        {
            await ValidarConflitoAsync(atual.MedicoId, req.NovoInicio, fim, atual.Id, ct);
            atual.AlterarStatus(StatusAgendamento.Remarcado, usuario.UsuarioId, req.Observacao);
            var novo = new Agendamento
            {
                PacienteId = atual.PacienteId, MedicoId = atual.MedicoId, EspecialidadeId = atual.EspecialidadeId,
                PacientePlanoId = atual.PacientePlanoId, TipoAtendimentoId = atual.TipoAtendimentoId,
                Inicio = req.NovoInicio, Fim = fim, Valor = atual.Valor, Observacoes = atual.Observacoes,
                AgendamentoOrigemId = atual.Id, CriadoPorUsuarioId = usuario.UsuarioId
            };
            novo.Historico.Add(new AgendamentoHistoricoStatus { StatusNovoId = StatusAgendamento.Agendado, UsuarioId = usuario.UsuarioId });
            db.Agendamentos.Add(novo);
            await db.SaveChangesAsync(ct);
            return novo.Id;
        }, ct);
    }

    public Task<List<BloqueioDto>> ListarBloqueiosAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return db.BloqueiosAgenda.AsNoTracking().Where(b => b.Inicio < fim && b.Fim > ini).OrderBy(b => b.Inicio)
            .Select(b => new BloqueioDto(b.Id, b.MedicoId, b.Inicio, b.Fim, b.Motivo)).ToListAsync(ct);
    }

    public async Task<int> BloquearAsync(BloqueioDto dto, CancellationToken ct)
    {
        if (dto.Fim <= dto.Inicio) throw new DomainException("Período inválido.");
        if (dto.MedicoId is int medicoId && !await db.Medicos.AnyAsync(m => m.Id == medicoId, ct))
            throw new DomainException("Médico não encontrado.");
        var b = new BloqueioAgenda { MedicoId = dto.MedicoId, Inicio = dto.Inicio, Fim = dto.Fim, Motivo = dto.Motivo, CriadoPorUsuarioId = usuario.UsuarioId };
        db.BloqueiosAgenda.Add(b);
        await db.SaveChangesAsync(ct);
        return b.Id;
    }

    private async Task ValidarConflitoAsync(int medicoId, DateTime inicio, DateTime fim, int? ignorarId, CancellationToken ct)
    {
        if (fim <= inicio) throw new DomainException("Horário inválido.");
        var conflito = await db.Agendamentos.AnyAsync(a => a.MedicoId == medicoId && a.Id != ignorarId
            && Ocupantes.Contains(a.StatusId) && a.Inicio < fim && a.Fim > inicio, ct);
        if (conflito) throw new DomainException("O médico já tem agendamento nesse horário.");

        var bloqueado = await db.BloqueiosAgenda.AnyAsync(b => (b.MedicoId == null || b.MedicoId == medicoId)
            && b.Inicio < fim && b.Fim > inicio, ct);
        if (bloqueado) throw new DomainException("Horário bloqueado na agenda.");
    }

    /// <summary>Sem a transação "agenda de todos os médicos", o usuário só enxerga a agenda do médico vinculado a ele.</summary>
    private int? RestringirAoMedicoLogado(int? medicoId) =>
        usuario.Pode(Transacoes.AgendaTodosMedicos) ? medicoId : usuario.MedicoId ?? -1;
}
