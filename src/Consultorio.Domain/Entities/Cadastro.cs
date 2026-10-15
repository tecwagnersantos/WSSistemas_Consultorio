using Consultorio.Domain.Common;

namespace Consultorio.Domain.Entities;

public class Especialidade : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public string Nome { get; set; } = "";
    public string? CodigoCbo { get; set; }
    public bool Ativo { get; set; } = true;
}

public class Medico : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int? UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public string Crm { get; set; } = "";
    public string UfCrm { get; set; } = "";
    public string? Cpf { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public short DuracaoConsultaPadraoMin { get; set; } = 30;
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public ICollection<MedicoEspecialidade> Especialidades { get; set; } = [];
}

public class MedicoEspecialidade : IPertenceClinica
{
    public int MedicoId { get; set; }
    public int ClinicaId { get; set; }
    public int EspecialidadeId { get; set; }
    public string? Rqe { get; set; }
    public bool Principal { get; set; }
    public Especialidade Especialidade { get; set; } = null!;
}

public class Convenio : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public string Nome { get; set; } = "";
    public string? RegistroAns { get; set; }
    public string? Cnpj { get; set; }
    public string? Telefone { get; set; }
    public short PrazoRetornoDias { get; set; } = 30;
    public bool Ativo { get; set; } = true;

    public ICollection<Plano> Planos { get; set; } = [];
}

public class Plano : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int ConvenioId { get; set; }
    public string Nome { get; set; } = "";
    public string? Codigo { get; set; }
    public bool Ativo { get; set; } = true;
    public Convenio Convenio { get; set; } = null!;
}

public class Paciente : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public string Nome { get; set; } = "";
    public string? NomeSocial { get; set; }
    public string? Cpf { get; set; }
    public string? Cns { get; set; }
    public DateOnly DataNascimento { get; set; }
    public string Sexo { get; set; } = "I";
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Celular { get; set; }
    public string? Cep { get; set; }
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? NomeResponsavel { get; set; }
    public string? CpfResponsavel { get; set; }
    public string? Observacoes { get; set; }
    public DateTime? ConsentimentoLgpdEm { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public ICollection<PacientePlano> Planos { get; set; } = [];
}

public class PacientePlano : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int PacienteId { get; set; }
    public int PlanoId { get; set; }
    public string NumeroCarteirinha { get; set; } = "";
    public DateOnly? ValidadeCarteirinha { get; set; }
    public bool Titular { get; set; } = true;
    public bool Ativo { get; set; } = true;
    public Plano Plano { get; set; } = null!;
}
