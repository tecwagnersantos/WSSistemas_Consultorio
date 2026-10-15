namespace Consultorio.Domain.Enums;

/// <summary>IDs fixos de agenda.StatusAgendamento.</summary>
public enum StatusAgendamento : byte
{
    Agendado = 1,
    Confirmado = 2,
    Aguardando = 3,
    EmAtendimento = 4,
    Atendido = 5,
    Faltou = 6,
    Cancelado = 7,
    Remarcado = 8
}

public static class StatusAgendamentoExtensions
{
    /// <summary>Status que contam como horário ocupado (OcupaHorario = 1).</summary>
    public static bool OcupaHorario(this StatusAgendamento s) => (byte)s <= 5;
}
