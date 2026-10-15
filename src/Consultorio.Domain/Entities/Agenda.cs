using Consultorio.Domain.Common;
using Consultorio.Domain.Enums;

namespace Consultorio.Domain.Entities;

public class HorarioAtendimento : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int MedicoId { get; set; }
    public int? EspecialidadeId { get; set; }
    public byte DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFim { get; set; }
    public short DuracaoSlotMin { get; set; }
    public DateOnly VigenciaInicio { get; set; }
    public DateOnly? VigenciaFim { get; set; }
    public bool Ativo { get; set; } = true;
}

public class BloqueioAgenda : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int? MedicoId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public string? Motivo { get; set; }
    public int? CriadoPorUsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
}

public class Agendamento : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int PacienteId { get; set; }
    public int MedicoId { get; set; }
    public int EspecialidadeId { get; set; }
    public int? PacientePlanoId { get; set; }
    public TipoAtendimento TipoAtendimentoId { get; set; } = TipoAtendimento.Consulta;
    public StatusAgendamento StatusId { get; private set; } = StatusAgendamento.Agendado;
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public decimal? Valor { get; set; }
    public string? Observacoes { get; set; }
    public int? AgendamentoOrigemId { get; set; }
    public DateTime? ConfirmadoEm { get; private set; }
    public DateTime? ChegadaEm { get; private set; }
    public DateTime? InicioAtendimentoEm { get; private set; }
    public DateTime? FimAtendimentoEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public int? CriadoPorUsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }
    public byte[] VersaoLinha { get; set; } = [];

    public Paciente Paciente { get; set; } = null!;
    public Medico Medico { get; set; } = null!;
    public Especialidade Especialidade { get; set; } = null!;
    public PacientePlano? PacientePlano { get; set; }
    public ICollection<AgendamentoHistoricoStatus> Historico { get; set; } = [];

    private static readonly Dictionary<StatusAgendamento, StatusAgendamento[]> Transicoes = new()
    {
        [StatusAgendamento.Agendado]      = [StatusAgendamento.Confirmado, StatusAgendamento.Aguardando, StatusAgendamento.Faltou, StatusAgendamento.Cancelado, StatusAgendamento.Remarcado],
        [StatusAgendamento.Confirmado]    = [StatusAgendamento.Aguardando, StatusAgendamento.Faltou, StatusAgendamento.Cancelado, StatusAgendamento.Remarcado],
        [StatusAgendamento.Aguardando]    = [StatusAgendamento.EmAtendimento, StatusAgendamento.Cancelado],
        [StatusAgendamento.EmAtendimento] = [StatusAgendamento.Atendido],
    };

    /// <summary>Muda o status validando o fluxo e registra o histórico.</summary>
    public void AlterarStatus(StatusAgendamento novo, int? usuarioId, string? observacao = null)
    {
        if (!Transicoes.TryGetValue(StatusId, out var permitidos) || !permitidos.Contains(novo))
            throw new DomainException("Não é possível passar de {0} para {1}.", StatusId, novo);

        var agora = DateTime.Now;
        switch (novo)
        {
            case StatusAgendamento.Confirmado: ConfirmadoEm = agora; break;
            case StatusAgendamento.Aguardando: ChegadaEm = agora; break;
            case StatusAgendamento.EmAtendimento: InicioAtendimentoEm = agora; break;
            case StatusAgendamento.Atendido: FimAtendimentoEm = agora; break;
            case StatusAgendamento.Cancelado:
                CanceladoEm = agora;
                MotivoCancelamento = observacao;
                break;
        }

        Historico.Add(new AgendamentoHistoricoStatus
        {
            StatusAnteriorId = StatusId,
            StatusNovoId = novo,
            UsuarioId = usuarioId,
            Observacao = observacao,
            AlteradoEm = agora
        });
        StatusId = novo;
        AtualizadoEm = agora;
    }
}

public class AgendamentoHistoricoStatus
{
    public long Id { get; set; }
    public int AgendamentoId { get; set; }
    public StatusAgendamento? StatusAnteriorId { get; set; }
    public StatusAgendamento StatusNovoId { get; set; }
    public int? UsuarioId { get; set; }
    public string? Observacao { get; set; }
    public DateTime AlteradoEm { get; set; } = DateTime.Now;
}
