using System.ComponentModel.DataAnnotations;
using Consultorio.Application.Pacientes;
using Consultorio.Application.Prontuario;
using Consultorio.Domain.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Consultorio.Web.Models;

public class ProntuarioVM
{
    public PacienteDto Paciente { get; set; } = new();
    public ResumoClinicoDto? Resumo { get; set; }
    public List<EvolucaoDto> Evolucoes { get; set; } = [];
    public List<PrescricaoDto> Prescricoes { get; set; } = [];
    public List<AnexoDto> Anexos { get; set; } = [];
    public int? MedicoLogadoId { get; set; }
    public int? UsuarioLogadoId { get; set; }
    public int? AgendamentoId { get; set; }
}

public class EvolucaoFormVM
{
    public int PacienteId { get; set; }
    public int? Id { get; set; }
    public int? AgendamentoId { get; set; }
    public int? EvolucaoRetificadaId { get; set; }

    [MaxLength(500)] public string? QueixaPrincipal { get; set; }
    public string? HistoriaDoencaAtual { get; set; }
    public string? ExameFisico { get; set; }
    [MaxLength(1000)] public string? HipoteseDiagnostica { get; set; }
    public string? Conduta { get; set; }

    [Range(0.5, 400, ErrorMessage = "Peso inválido.")] public decimal? PesoKg { get; set; }
    [Range(20, 250, ErrorMessage = "Altura inválida.")] public decimal? AlturaCm { get; set; }
    [Range(40, 300, ErrorMessage = "Pressão inválida.")] public short? PressaoSistolica { get; set; }
    [Range(20, 200, ErrorMessage = "Pressão inválida.")] public short? PressaoDiastolica { get; set; }
    [Range(20, 250, ErrorMessage = "Frequência inválida.")] public short? FrequenciaCardiaca { get; set; }
    [Range(30, 45, ErrorMessage = "Temperatura inválida.")] public decimal? TemperaturaC { get; set; }
    [Range(50, 100, ErrorMessage = "Saturação inválida.")] public byte? SaturacaoO2 { get; set; }

    /// <summary>"rascunho" ou "assinar".</summary>
    public string? Acao { get; set; }

    [BindNever] public string PacienteNome { get; set; } = "";
    [BindNever] public EvolucaoDto? Retificada { get; set; }

    public EvolucaoSalvarRequest ParaRequest() => new(AgendamentoId, EvolucaoRetificadaId, QueixaPrincipal,
        HistoriaDoencaAtual, ExameFisico, HipoteseDiagnostica, Conduta, PesoKg, AlturaCm, PressaoSistolica,
        PressaoDiastolica, FrequenciaCardiaca, TemperaturaC, SaturacaoO2);

    public static EvolucaoFormVM De(EvolucaoDto e) => new()
    {
        PacienteId = e.PacienteId, Id = e.Id, AgendamentoId = e.AgendamentoId, EvolucaoRetificadaId = e.EvolucaoRetificadaId,
        QueixaPrincipal = e.QueixaPrincipal, HistoriaDoencaAtual = e.HistoriaDoencaAtual, ExameFisico = e.ExameFisico,
        HipoteseDiagnostica = e.HipoteseDiagnostica, Conduta = e.Conduta, PesoKg = e.PesoKg, AlturaCm = e.AlturaCm,
        PressaoSistolica = e.PressaoSistolica, PressaoDiastolica = e.PressaoDiastolica,
        FrequenciaCardiaca = e.FrequenciaCardiaca, TemperaturaC = e.TemperaturaC, SaturacaoO2 = e.SaturacaoO2
    };
}

public class ResumoClinicoFormVM
{
    [MaxLength(3)] public string? TipoSanguineo { get; set; }
    [MaxLength(1000)] public string? Alergias { get; set; }
    [MaxLength(1000)] public string? Comorbidades { get; set; }
    [MaxLength(1000)] public string? MedicamentosEmUso { get; set; }
    [MaxLength(2000)] public string? AntecedentesPessoais { get; set; }
    [MaxLength(2000)] public string? AntecedentesFamiliares { get; set; }
    [MaxLength(1000)] public string? Habitos { get; set; }

    public ResumoClinicoDto ParaDto() => new(TipoSanguineo, Alergias, Comorbidades, MedicamentosEmUso,
        AntecedentesPessoais, AntecedentesFamiliares, Habitos, null);
}

public class PrescricaoFormVM
{
    public int PacienteId { get; set; }
    public int? EvolucaoId { get; set; }

    /// <summary>S = simples, C = controle especial, A = antimicrobiano.</summary>
    [RegularExpression("^[SCA]$")] public string Tipo { get; set; } = "S";
    [Range(1, 365, ErrorMessage = "Validade entre 1 e 365 dias.")] public short? ValidadeDias { get; set; }
    [MaxLength(500)] public string? Observacoes { get; set; }
    public List<PrescricaoItemFormVM> Itens { get; set; } = [];

    [BindNever] public string PacienteNome { get; set; } = "";

    public PrescricaoDto ParaDto() => new(0, EvolucaoId, Tipo, DateTime.Now, ValidadeDias, Observacoes,
        Itens.Where(i => !string.IsNullOrWhiteSpace(i.Medicamento))
            .Select((i, n) => new PrescricaoItemDto((byte)(n + 1), i.Medicamento!.Trim(), i.Concentracao, i.FormaFarmaceutica,
                i.ViaAdministracao, i.Posologia ?? "", i.Quantidade, i.UsoContinuo)).ToList());
}

public class PrescricaoItemFormVM
{
    public string? Medicamento { get; set; }
    public string? Concentracao { get; set; }
    public string? FormaFarmaceutica { get; set; }
    public string? ViaAdministracao { get; set; }
    public string? Posologia { get; set; }
    public string? Quantidade { get; set; }
    public bool UsoContinuo { get; set; }
}

public class AnexoFormVM
{
    public string Tipo { get; set; } = TiposAnexo.Exame;
    public string? Descricao { get; set; }
    public int? EvolucaoId { get; set; }
    public List<IFormFile> Arquivos { get; set; } = [];
}
