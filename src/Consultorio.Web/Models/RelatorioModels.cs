using Consultorio.Application.Cadastros;
using Consultorio.Application.Relatorios;

namespace Consultorio.Web.Models;

public class DashboardVM
{
    public DashboardDto Dados { get; set; } = null!;
    public int? MedicoId { get; set; }
    public bool PodeEscolherMedico { get; set; }
    public List<MedicoDto> Medicos { get; set; } = [];
}

public class AtendimentosVM
{
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public int? MedicoId { get; set; }
    public string? Status { get; set; }
    public bool PodeEscolherMedico { get; set; }
    public List<MedicoDto> Medicos { get; set; } = [];
    public IReadOnlyList<AtendimentoRelatorioDto> Itens { get; set; } = [];
}

public class IndicadoresVM
{
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public int? MedicoId { get; set; }
    public bool PodeEscolherMedico { get; set; }
    public List<MedicoDto> Medicos { get; set; } = [];
    public IReadOnlyList<IndicadorMensalDto> Itens { get; set; } = [];
}
