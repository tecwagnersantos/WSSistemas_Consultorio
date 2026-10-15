using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Cadastros;

public record EspecialidadeDto(int Id, string Nome, string? CodigoCbo, bool Ativo);
public record MedicoEspecialidadeDto(int EspecialidadeId, string? Especialidade, string? Rqe, bool Principal);
public record MedicoDto(int Id, int? UsuarioId, string Nome, string Crm, string UfCrm, string? Cpf, string? Email,
    string? Telefone, short DuracaoConsultaPadraoMin, bool Ativo, List<MedicoEspecialidadeDto> Especialidades);
public record PlanoDto(int Id, string Nome, string? Codigo, bool Ativo);
public record ConvenioDto(int Id, string Nome, string? RegistroAns, string? Cnpj, string? Telefone,
    short PrazoRetornoDias, bool Ativo, List<PlanoDto> Planos);
public record HorarioAtendimentoDto(int Id, int MedicoId, int? EspecialidadeId, byte DiaSemana, TimeOnly HoraInicio,
    TimeOnly HoraFim, short DuracaoSlotMin, DateOnly VigenciaInicio, DateOnly? VigenciaFim, bool Ativo);

public class CadastroService(IAppDbContext db, IUsuarioAtual usuario, LimitesPlano limites)
{
    // ---------- Especialidades ----------
    public Task<List<EspecialidadeDto>> ListarEspecialidadesAsync(CancellationToken ct) =>
        db.Especialidades.AsNoTracking().OrderBy(e => e.Nome)
            .Select(e => new EspecialidadeDto(e.Id, e.Nome, e.CodigoCbo, e.Ativo)).ToListAsync(ct);

    public async Task<int> SalvarEspecialidadeAsync(int? id, EspecialidadeDto dto, CancellationToken ct)
    {
        var e = id is null ? db.Especialidades.Add(new Especialidade()).Entity
            : await db.Especialidades.FindAsync([id], ct) ?? throw new NaoEncontradoException("Especialidade não encontrada.");
        e.Nome = dto.Nome.Trim(); e.CodigoCbo = dto.CodigoCbo; e.Ativo = dto.Ativo;
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    // ---------- Médicos ----------
    public Task<List<MedicoDto>> ListarMedicosAsync(bool somenteAtivos, CancellationToken ct) =>
        db.Medicos.AsNoTracking().Where(m => !somenteAtivos || m.Ativo).OrderBy(m => m.Nome)
            .Select(m => new MedicoDto(m.Id, m.UsuarioId, m.Nome, m.Crm, m.UfCrm, m.Cpf, m.Email, m.Telefone,
                m.DuracaoConsultaPadraoMin, m.Ativo,
                m.Especialidades.Select(e => new MedicoEspecialidadeDto(e.EspecialidadeId, e.Especialidade.Nome, e.Rqe, e.Principal)).ToList()))
            .ToListAsync(ct);

    public async Task<int> SalvarMedicoAsync(int? id, MedicoDto dto, CancellationToken ct)
    {
        if (await db.Medicos.AnyAsync(m => m.Crm == dto.Crm && m.UfCrm == dto.UfCrm && m.Id != id, ct))
            throw new DomainException("Já existe médico com este CRM/UF.");
        // Os filtros globais garantem que usuário e especialidades são da mesma clínica
        if (dto.UsuarioId is int uid && !await db.Usuarios.AnyAsync(u => u.Id == uid, ct))
            throw new DomainException("Usuário não encontrado.");
        var especialidades = dto.Especialidades.Select(e => e.EspecialidadeId).Distinct().ToList();
        if (await db.Especialidades.CountAsync(e => especialidades.Contains(e.Id), ct) != especialidades.Count)
            throw new DomainException("Especialidade não encontrada.");

        var existente = id is null ? null
            : await db.Medicos.Include(x => x.Especialidades).FirstOrDefaultAsync(x => x.Id == id, ct)
              ?? throw new NaoEncontradoException("Médico não encontrado.");
        // Limite de médicos do plano: conta ao incluir ativo ou ao reativar (antes de alterar qualquer coisa)
        if (dto.Ativo && existente is not { Ativo: true })
            await limites.GarantirVagaMedicoAsync(usuario.ClinicaId ?? throw new DomainException("Não há clínica definida."), ct);
        var m = existente ?? db.Medicos.Add(new Medico()).Entity;
        m.UsuarioId = dto.UsuarioId; m.Nome = dto.Nome.Trim(); m.Crm = dto.Crm; m.UfCrm = dto.UfCrm.ToUpperInvariant();
        m.Cpf = dto.Cpf; m.Email = dto.Email; m.Telefone = dto.Telefone;
        m.DuracaoConsultaPadraoMin = dto.DuracaoConsultaPadraoMin; m.Ativo = dto.Ativo;
        if (id != null) m.AtualizadoEm = DateTime.Now;

        m.Especialidades.Clear();
        foreach (var e in dto.Especialidades)
            m.Especialidades.Add(new MedicoEspecialidade { EspecialidadeId = e.EspecialidadeId, Rqe = e.Rqe, Principal = e.Principal });

        await db.SaveChangesAsync(ct);
        return m.Id;
    }

    // ---------- Convênios ----------
    public Task<List<ConvenioDto>> ListarConveniosAsync(CancellationToken ct) =>
        db.Convenios.AsNoTracking().OrderBy(c => c.Nome)
            .Select(c => new ConvenioDto(c.Id, c.Nome, c.RegistroAns, c.Cnpj, c.Telefone, c.PrazoRetornoDias, c.Ativo,
                c.Planos.OrderBy(p => p.Nome).Select(p => new PlanoDto(p.Id, p.Nome, p.Codigo, p.Ativo)).ToList()))
            .ToListAsync(ct);

    public async Task<int> SalvarConvenioAsync(int? id, ConvenioDto dto, CancellationToken ct)
    {
        var c = id is null ? db.Convenios.Add(new Convenio()).Entity
            : await db.Convenios.Include(x => x.Planos).FirstOrDefaultAsync(x => x.Id == id, ct)
              ?? throw new NaoEncontradoException("Convênio não encontrado.");
        c.Nome = dto.Nome.Trim(); c.RegistroAns = dto.RegistroAns; c.Cnpj = dto.Cnpj; c.Telefone = dto.Telefone;
        c.PrazoRetornoDias = dto.PrazoRetornoDias; c.Ativo = dto.Ativo;

        foreach (var p in c.Planos)
        {
            var d = dto.Planos.FirstOrDefault(x => x.Id == p.Id);
            if (d is null) { p.Ativo = false; continue; }
            p.Nome = d.Nome; p.Codigo = d.Codigo; p.Ativo = d.Ativo;
        }
        foreach (var d in dto.Planos.Where(x => x.Id == 0))
            c.Planos.Add(new Plano { Nome = d.Nome, Codigo = d.Codigo, Ativo = d.Ativo });

        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    // ---------- Grade de horários ----------
    public Task<List<HorarioAtendimentoDto>> ListarHorariosAsync(int medicoId, CancellationToken ct) =>
        db.HorariosAtendimento.AsNoTracking().Where(h => h.MedicoId == medicoId)
            .OrderBy(h => h.DiaSemana).ThenBy(h => h.HoraInicio)
            .Select(h => new HorarioAtendimentoDto(h.Id, h.MedicoId, h.EspecialidadeId, h.DiaSemana, h.HoraInicio,
                h.HoraFim, h.DuracaoSlotMin, h.VigenciaInicio, h.VigenciaFim, h.Ativo))
            .ToListAsync(ct);

    public async Task<int> SalvarHorarioAsync(int? id, HorarioAtendimentoDto dto, CancellationToken ct)
    {
        if (dto.HoraFim <= dto.HoraInicio) throw new DomainException("Hora final deve ser maior que a inicial.");
        if (!await db.Medicos.AnyAsync(m => m.Id == dto.MedicoId, ct))
            throw new DomainException("Médico não encontrado.");
        if (dto.EspecialidadeId is int esp && !await db.Especialidades.AnyAsync(e => e.Id == esp, ct))
            throw new DomainException("Especialidade não encontrada.");
        var h = id is null ? db.HorariosAtendimento.Add(new HorarioAtendimento()).Entity
            : await db.HorariosAtendimento.FindAsync([id], ct) ?? throw new NaoEncontradoException("Horário não encontrado.");
        h.MedicoId = dto.MedicoId; h.EspecialidadeId = dto.EspecialidadeId; h.DiaSemana = dto.DiaSemana;
        h.HoraInicio = dto.HoraInicio; h.HoraFim = dto.HoraFim; h.DuracaoSlotMin = dto.DuracaoSlotMin;
        h.VigenciaInicio = dto.VigenciaInicio; h.VigenciaFim = dto.VigenciaFim; h.Ativo = dto.Ativo;
        await db.SaveChangesAsync(ct);
        return h.Id;
    }
}
