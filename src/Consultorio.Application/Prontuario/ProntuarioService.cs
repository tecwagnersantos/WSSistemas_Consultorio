using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Consultorio.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Prontuario;

public record ResumoClinicoDto(string? TipoSanguineo, string? Alergias, string? Comorbidades, string? MedicamentosEmUso,
    string? AntecedentesPessoais, string? AntecedentesFamiliares, string? Habitos, DateTime? AtualizadoEm);

public record EvolucaoDto(int Id, int PacienteId, int MedicoId, string? Medico, int? AgendamentoId, int? EvolucaoRetificadaId,
    DateTime DataHora, string? QueixaPrincipal, string? HistoriaDoencaAtual, string? ExameFisico, string? HipoteseDiagnostica,
    string? Conduta, decimal? PesoKg, decimal? AlturaCm, short? PressaoSistolica, short? PressaoDiastolica,
    short? FrequenciaCardiaca, decimal? TemperaturaC, byte? SaturacaoO2, string Situacao, DateTime? AssinadaEm);

public record EvolucaoSalvarRequest(int? AgendamentoId, int? EvolucaoRetificadaId, string? QueixaPrincipal,
    string? HistoriaDoencaAtual, string? ExameFisico, string? HipoteseDiagnostica, string? Conduta, decimal? PesoKg,
    decimal? AlturaCm, short? PressaoSistolica, short? PressaoDiastolica, short? FrequenciaCardiaca,
    decimal? TemperaturaC, byte? SaturacaoO2);

public record PrescricaoItemDto(byte Ordem, string Medicamento, string? Concentracao, string? FormaFarmaceutica,
    string? ViaAdministracao, string Posologia, string? Quantidade, bool UsoContinuo);
public record PrescricaoDto(int Id, int? EvolucaoId, string Tipo, DateTime EmitidaEm, short? ValidadeDias,
    string? Observacoes, List<PrescricaoItemDto> Itens);

public record PrescricaoImpressaoDto(int Id, DateTime EmitidaEm, string Tipo, short? ValidadeDias, string? Observacoes,
    string Paciente, string? CpfPaciente, DateOnly DataNascimento, string Medico, string Crm, string UfCrm,
    List<PrescricaoItemDto> Itens, string Clinica, string? EnderecoClinica, string? CidadeClinica, string? TelefoneClinica);

/// <summary>Prontuário: acesso só para o perfil Médico; toda leitura e escrita é auditada (LGPD).</summary>
public class ProntuarioService(IAppDbContext db, IUsuarioAtual usuario, IAuditoria auditoria)
{
    private int MedicoLogado => usuario.MedicoId
        ?? throw new DomainException("Usuário não está vinculado a um cadastro de médico.");

    public async Task<ResumoClinicoDto?> ObterResumoAsync(int pacienteId, CancellationToken ct)
    {
        var r = await db.ResumosClinicos.AsNoTracking().FirstOrDefaultAsync(x => x.PacienteId == pacienteId, ct);
        auditoria.Registrar("LEITURA", "prontuario.ResumoClinico", pacienteId);
        await db.SaveChangesAsync(ct);
        return r is null ? null : new(r.TipoSanguineo, r.Alergias, r.Comorbidades, r.MedicamentosEmUso,
            r.AntecedentesPessoais, r.AntecedentesFamiliares, r.Habitos, r.AtualizadoEm);
    }

    public async Task SalvarResumoAsync(int pacienteId, ResumoClinicoDto dto, CancellationToken ct)
    {
        if (!await db.Pacientes.AnyAsync(p => p.Id == pacienteId, ct))
            throw new NaoEncontradoException("Paciente não encontrado.");
        var r = await db.ResumosClinicos.FirstOrDefaultAsync(x => x.PacienteId == pacienteId, ct);
        if (r is null) { r = new ResumoClinico { PacienteId = pacienteId }; db.ResumosClinicos.Add(r); }
        r.TipoSanguineo = dto.TipoSanguineo; r.Alergias = dto.Alergias; r.Comorbidades = dto.Comorbidades;
        r.MedicamentosEmUso = dto.MedicamentosEmUso; r.AntecedentesPessoais = dto.AntecedentesPessoais;
        r.AntecedentesFamiliares = dto.AntecedentesFamiliares; r.Habitos = dto.Habitos;
        r.AtualizadoPorUsuarioId = usuario.UsuarioId; r.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO", "prontuario.ResumoClinico", pacienteId, novo: dto);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<EvolucaoDto>> ListarEvolucoesAsync(int pacienteId, CancellationToken ct)
    {
        var lista = await db.Evolucoes.AsNoTracking().Where(e => e.PacienteId == pacienteId)
            .OrderByDescending(e => e.DataHora)
            .Select(e => new EvolucaoDto(e.Id, e.PacienteId, e.MedicoId, e.Medico.Nome, e.AgendamentoId, e.EvolucaoRetificadaId,
                e.DataHora, e.QueixaPrincipal, e.HistoriaDoencaAtual, e.ExameFisico, e.HipoteseDiagnostica, e.Conduta,
                e.PesoKg, e.AlturaCm, e.PressaoSistolica, e.PressaoDiastolica, e.FrequenciaCardiaca, e.TemperaturaC,
                e.SaturacaoO2, e.Situacao, e.AssinadaEm))
            .ToListAsync(ct);
        auditoria.Registrar("LEITURA", "prontuario.Evolucao", $"paciente:{pacienteId}");
        await db.SaveChangesAsync(ct);
        return lista;
    }

    public async Task<EvolucaoDto> ObterEvolucaoAsync(int pacienteId, int evolucaoId, CancellationToken ct)
    {
        var e = await db.Evolucoes.AsNoTracking().Where(x => x.Id == evolucaoId && x.PacienteId == pacienteId)
            .Select(e => new EvolucaoDto(e.Id, e.PacienteId, e.MedicoId, e.Medico.Nome, e.AgendamentoId, e.EvolucaoRetificadaId,
                e.DataHora, e.QueixaPrincipal, e.HistoriaDoencaAtual, e.ExameFisico, e.HipoteseDiagnostica, e.Conduta,
                e.PesoKg, e.AlturaCm, e.PressaoSistolica, e.PressaoDiastolica, e.FrequenciaCardiaca, e.TemperaturaC,
                e.SaturacaoO2, e.Situacao, e.AssinadaEm))
            .FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Evolução não encontrada.");
        auditoria.Registrar("LEITURA", "prontuario.Evolucao", evolucaoId);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task<int> CriarEvolucaoAsync(int pacienteId, EvolucaoSalvarRequest req, CancellationToken ct)
    {
        var medicoId = MedicoLogado;
        if (!await db.Pacientes.AnyAsync(p => p.Id == pacienteId, ct))
            throw new NaoEncontradoException("Paciente não encontrado.");
        if (req.EvolucaoRetificadaId is int retId &&
            !await db.Evolucoes.AnyAsync(e => e.Id == retId && e.PacienteId == pacienteId && e.Situacao == Evolucao.Assinada, ct))
            throw new DomainException("Só é possível retificar uma evolução assinada do mesmo paciente.");
        if (req.AgendamentoId is int agendamento && !await db.Agendamentos.AnyAsync(a => a.Id == agendamento && a.PacienteId == pacienteId, ct))
            throw new DomainException("Agendamento não pertence ao paciente.");

        var e = new Evolucao { PacienteId = pacienteId, MedicoId = medicoId, EvolucaoRetificadaId = req.EvolucaoRetificadaId };
        Aplicar(e, req);
        db.Evolucoes.Add(e);

        // Abrir a evolução a partir do agendamento coloca o paciente "em atendimento"
        if (req.AgendamentoId is int agId && req.EvolucaoRetificadaId is null)
        {
            var ag = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == agId && a.PacienteId == pacienteId, ct)
                ?? throw new DomainException("Agendamento não pertence ao paciente.");
            if (ag.StatusId == StatusAgendamento.Aguardando) ag.AlterarStatus(StatusAgendamento.EmAtendimento, usuario.UsuarioId);
        }

        await db.SaveChangesAsync(ct);
        auditoria.Registrar("INCLUSAO", "prontuario.Evolucao", e.Id);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task AtualizarRascunhoAsync(int evolucaoId, EvolucaoSalvarRequest req, CancellationToken ct)
    {
        var e = await ObterDoMedicoAsync(evolucaoId, ct);
        e.GarantirEditavel();
        Aplicar(e, req);
        e.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO", "prontuario.Evolucao", e.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Assina a evolução (fica imutável) e conclui o atendimento do agendamento vinculado.</summary>
    public async Task AssinarAsync(int evolucaoId, CancellationToken ct)
    {
        var e = await ObterDoMedicoAsync(evolucaoId, ct);
        e.Assinar();
        if (e.AgendamentoId is int agId)
        {
            var ag = await db.Agendamentos.FirstAsync(a => a.Id == agId, ct);
            if (ag.StatusId == StatusAgendamento.EmAtendimento) ag.AlterarStatus(StatusAgendamento.Atendido, usuario.UsuarioId);
        }
        auditoria.Registrar("ALTERACAO", "prontuario.Evolucao", e.Id, novo: new { Situacao = "A", e.AssinadaEm });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<PrescricaoDto>> ListarPrescricoesAsync(int pacienteId, CancellationToken ct)
    {
        var lista = await db.Prescricoes.AsNoTracking().Where(p => p.PacienteId == pacienteId)
            .OrderByDescending(p => p.EmitidaEm)
            .Select(p => new PrescricaoDto(p.Id, p.EvolucaoId, p.Tipo, p.EmitidaEm, p.ValidadeDias, p.Observacoes,
                p.Itens.OrderBy(i => i.Ordem).Select(i => new PrescricaoItemDto(i.Ordem, i.Medicamento, i.Concentracao,
                    i.FormaFarmaceutica, i.ViaAdministracao, i.Posologia, i.Quantidade, i.UsoContinuo)).ToList()))
            .ToListAsync(ct);
        auditoria.Registrar("LEITURA", "prontuario.Prescricao", $"paciente:{pacienteId}");
        await db.SaveChangesAsync(ct);
        return lista;
    }

    public async Task<int> EmitirPrescricaoAsync(int pacienteId, PrescricaoDto dto, CancellationToken ct)
    {
        if (dto.Itens.Count == 0) throw new DomainException("Inclua ao menos um medicamento.");
        if (!await db.Pacientes.AnyAsync(x => x.Id == pacienteId, ct))
            throw new NaoEncontradoException("Paciente não encontrado.");
        if (dto.EvolucaoId is int evo && !await db.Evolucoes.AnyAsync(e => e.Id == evo && e.PacienteId == pacienteId, ct))
            throw new DomainException("Evolução não pertence ao paciente.");
        var p = new Prescricao
        {
            PacienteId = pacienteId, MedicoId = MedicoLogado, EvolucaoId = dto.EvolucaoId, Tipo = dto.Tipo,
            ValidadeDias = dto.ValidadeDias, Observacoes = dto.Observacoes, AssinadaEm = DateTime.Now
        };
        byte ordem = 1;
        foreach (var i in dto.Itens)
            p.Itens.Add(new PrescricaoItem { Ordem = ordem++, Medicamento = i.Medicamento, Concentracao = i.Concentracao,
                FormaFarmaceutica = i.FormaFarmaceutica, ViaAdministracao = i.ViaAdministracao, Posologia = i.Posologia,
                Quantidade = i.Quantidade, UsoContinuo = i.UsoContinuo });
        db.Prescricoes.Add(p);
        await db.SaveChangesAsync(ct);
        auditoria.Registrar("INCLUSAO", "prontuario.Prescricao", p.Id);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    /// <summary>Dados para imprimir a receita.</summary>
    public async Task<PrescricaoImpressaoDto> ObterPrescricaoImpressaoAsync(int pacienteId, int prescricaoId, CancellationToken ct)
    {
        var r = await (from p in db.Prescricoes.AsNoTracking()
                       join pac in db.Pacientes on p.PacienteId equals pac.Id
                       join m in db.Medicos on p.MedicoId equals m.Id
                       join c in db.Clinicas on p.ClinicaId equals c.Id
                       where p.Id == prescricaoId && p.PacienteId == pacienteId
                       select new PrescricaoImpressaoDto(p.Id, p.EmitidaEm, p.Tipo, p.ValidadeDias, p.Observacoes,
                           pac.NomeSocial ?? pac.Nome, pac.Cpf, pac.DataNascimento, m.Nome, m.Crm, m.UfCrm,
                           p.Itens.OrderBy(i => i.Ordem).Select(i => new PrescricaoItemDto(i.Ordem, i.Medicamento, i.Concentracao,
                               i.FormaFarmaceutica, i.ViaAdministracao, i.Posologia, i.Quantidade, i.UsoContinuo)).ToList(),
                           c.Nome, c.Endereco, c.Cidade != null ? c.Cidade + "/" + c.Uf : null, c.Telefone))
            .FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Prescrição não encontrada.");
        auditoria.Registrar("IMPRESSAO", "prontuario.Prescricao", prescricaoId);
        await db.SaveChangesAsync(ct);
        return r;
    }

    private async Task<Evolucao> ObterDoMedicoAsync(int evolucaoId, CancellationToken ct)
    {
        var e = await db.Evolucoes.FirstOrDefaultAsync(x => x.Id == evolucaoId, ct)
            ?? throw new NaoEncontradoException("Evolução não encontrada.");
        if (e.MedicoId != MedicoLogado) throw new DomainException("Somente o médico autor pode alterar ou assinar a evolução.");
        return e;
    }

    private static void Aplicar(Evolucao e, EvolucaoSalvarRequest r)
    {
        e.AgendamentoId = r.AgendamentoId; e.QueixaPrincipal = r.QueixaPrincipal; e.HistoriaDoencaAtual = r.HistoriaDoencaAtual;
        e.ExameFisico = r.ExameFisico; e.HipoteseDiagnostica = r.HipoteseDiagnostica; e.Conduta = r.Conduta;
        e.PesoKg = r.PesoKg; e.AlturaCm = r.AlturaCm; e.PressaoSistolica = r.PressaoSistolica;
        e.PressaoDiastolica = r.PressaoDiastolica; e.FrequenciaCardiaca = r.FrequenciaCardiaca;
        e.TemperaturaC = r.TemperaturaC; e.SaturacaoO2 = r.SaturacaoO2;
    }
}
