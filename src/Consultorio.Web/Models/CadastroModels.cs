using System.ComponentModel.DataAnnotations;
using Consultorio.Application.Auth;
using Consultorio.Application.Cadastros;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Consultorio.Web.Models;

public class EspecialidadeFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(100)] public string Nome { get; set; } = "";
    [MaxLength(10)] public string? CodigoCbo { get; set; }
    public bool Ativo { get; set; } = true;
}

public class MedicoFormVM
{
    public int? Id { get; set; }
    public int? UsuarioId { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] public string Nome { get; set; } = "";
    [Required(ErrorMessage = "Informe o CRM."), MaxLength(15)] public string Crm { get; set; } = "";
    [Required(ErrorMessage = "Informe a UF do CRM."), StringLength(2, MinimumLength = 2, ErrorMessage = "UF com 2 letras.")]
    public string UfCrm { get; set; } = "";
    [RegularExpression(@"^\d{11}$", ErrorMessage = "CPF deve ter 11 dígitos.")] public string? Cpf { get; set; }
    [EmailAddress(ErrorMessage = "E-mail inválido."), MaxLength(254)] public string? Email { get; set; }
    [MaxLength(20)] public string? Telefone { get; set; }
    [Range(5, 240, ErrorMessage = "Duração entre 5 e 240 minutos.")] public short DuracaoConsultaPadraoMin { get; set; } = 30;
    public bool Ativo { get; set; } = true;
    public int? PrincipalId { get; set; }
    public List<MedicoEspecialidadeFormVM> Especialidades { get; set; } = [];

    [BindNever] public List<UsuarioDto> UsuariosMedicos { get; set; } = [];

    public MedicoDto ParaDto() => new(Id ?? 0, UsuarioId, Nome, Crm.Trim(), UfCrm, Cpf, Email, Telefone,
        DuracaoConsultaPadraoMin, Ativo,
        Especialidades.Where(e => e.Selecionada)
            .Select(e => new MedicoEspecialidadeDto(e.EspecialidadeId, null, e.Rqe, e.EspecialidadeId == PrincipalId)).ToList());
}

public class MedicoEspecialidadeFormVM
{
    public int EspecialidadeId { get; set; }
    public string Nome { get; set; } = "";
    public bool Selecionada { get; set; }
    public string? Rqe { get; set; }
}

public class ConvenioFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(100)] public string Nome { get; set; } = "";
    [MaxLength(10)] public string? RegistroAns { get; set; }
    [RegularExpression(@"^\d{14}$", ErrorMessage = "CNPJ deve ter 14 dígitos.")] public string? Cnpj { get; set; }
    [MaxLength(20)] public string? Telefone { get; set; }
    [Range(0, 365, ErrorMessage = "Prazo entre 0 e 365 dias.")] public short PrazoRetornoDias { get; set; } = 30;
    public bool Ativo { get; set; } = true;
    public List<PlanoFormVM> Planos { get; set; } = [];

    public ConvenioDto ParaDto() => new(Id ?? 0, Nome, RegistroAns, Cnpj, Telefone, PrazoRetornoDias, Ativo,
        Planos.Where(p => !string.IsNullOrWhiteSpace(p.Nome))
            .Select(p => new PlanoDto(p.Id, p.Nome!.Trim(), p.Codigo, p.Ativo)).ToList());
}

public class PlanoFormVM
{
    public int Id { get; set; }
    public string? Nome { get; set; }
    public string? Codigo { get; set; }
    public bool Ativo { get; set; } = true;
}

public class HorariosVM
{
    public MedicoDto Medico { get; set; } = null!;
    public List<HorarioAtendimentoDto> Horarios { get; set; } = [];
    public List<EspecialidadeDto> Especialidades { get; set; } = [];
}

public class HorarioFormVM
{
    public int? Id { get; set; }
    public int MedicoId { get; set; }
    public int? EspecialidadeId { get; set; }
    /// <summary>Na inclusão, um registro é criado para cada dia marcado.</summary>
    public List<byte> DiasSemana { get; set; } = [];
    [Required(ErrorMessage = "Informe a hora inicial.")] public TimeOnly? HoraInicio { get; set; }
    [Required(ErrorMessage = "Informe a hora final.")] public TimeOnly? HoraFim { get; set; }
    [Range(5, 240, ErrorMessage = "Intervalo entre 5 e 240 minutos.")] public short DuracaoSlotMin { get; set; } = 30;
    [Required(ErrorMessage = "Informe o início da vigência.")] public DateOnly? VigenciaInicio { get; set; }
    public DateOnly? VigenciaFim { get; set; }
    public bool Ativo { get; set; } = true;
}

public class UsuarioFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] public string Nome { get; set; } = "";
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] public string Email { get; set; } = "";
    public string? Senha { get; set; }
    public bool Ativo { get; set; } = true;
    public List<int> PerfilIds { get; set; } = [];
    public List<Consultorio.Application.Admin.PerfilOpcaoDto> Opcoes { get; set; } = [];
}
