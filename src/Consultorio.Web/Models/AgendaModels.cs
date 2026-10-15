using System.ComponentModel.DataAnnotations;
using Consultorio.Application.Agenda;
using Consultorio.Application.Cadastros;
using Consultorio.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Consultorio.Web.Models;

public class AgendaVM
{
    public DateOnly Data { get; set; }
    public string Visao { get; set; } = "dia";
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public int? MedicoId { get; set; }
    public bool PodeEscolherMedico { get; set; }
    public List<MedicoDto> Medicos { get; set; } = [];
    public List<AgendamentoDto> Itens { get; set; } = [];
    public List<BloqueioDto> Bloqueios { get; set; } = [];
}

public class NovoAgendamentoVM
{
    [Range(1, int.MaxValue, ErrorMessage = "Selecione o paciente.")]
    public int PacienteId { get; set; }
    public string? PacienteNome { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Selecione o médico.")]
    public int MedicoId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Selecione a especialidade.")]
    public int EspecialidadeId { get; set; }

    public int? PacientePlanoId { get; set; }
    public TipoAtendimento Tipo { get; set; } = TipoAtendimento.Consulta;

    [Required(ErrorMessage = "Informe o dia.")]
    public DateOnly? Dia { get; set; }

    [Required(ErrorMessage = "Escolha um horário livre ou informe a hora.")]
    public TimeOnly? Hora { get; set; }

    [Range(5, 480, ErrorMessage = "Duração entre 5 e 480 minutos.")]
    public int? DuracaoMin { get; set; }

    [Range(0, 999999, ErrorMessage = "Valor inválido.")]
    public decimal? Valor { get; set; }

    [MaxLength(500)]
    public string? Observacoes { get; set; }

    [BindNever] public List<MedicoDto> Medicos { get; set; } = [];
}

public class BloqueiosVM
{
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public List<MedicoDto> Medicos { get; set; } = [];
    public List<BloqueioDto> Itens { get; set; } = [];
}

public class BloqueioFormVM
{
    public int? MedicoId { get; set; }
    [Required(ErrorMessage = "Informe o início.")] public DateTime? Inicio { get; set; }
    [Required(ErrorMessage = "Informe o fim.")] public DateTime? Fim { get; set; }
    [MaxLength(200)] public string? Motivo { get; set; }
}

public record AlterarStatusVM(StatusAgendamento Status, string? Observacao);
public record RemarcarVM(DateTime NovoInicio, int? DuracaoMin, string? Observacao);
