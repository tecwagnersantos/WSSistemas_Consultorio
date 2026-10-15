using System.ComponentModel.DataAnnotations;
using Consultorio.Application.Admin;

namespace Consultorio.Web.Areas.Admin.Models;

public class ClienteFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] public string Nome { get; set; } = "";
    public string? Cnpj { get; set; }
    [MaxLength(20)] public string? Telefone { get; set; }
    [EmailAddress(ErrorMessage = "E-mail inválido."), MaxLength(254)] public string? Email { get; set; }
    [MaxLength(200)] public string? Endereco { get; set; }
    [MaxLength(80)] public string? Cidade { get; set; }
    [StringLength(2, MinimumLength = 2, ErrorMessage = "UF com 2 letras.")] public string? Uf { get; set; }
    [Range(0, 9999, ErrorMessage = "Limite inválido.")] public short? MaxMedicos { get; set; }
    [Range(0, 9999, ErrorMessage = "Limite inválido.")] public short? MaxUsuarios { get; set; }
    [MaxLength(500)] public string? Observacoes { get; set; }
    /// <summary>ATIVO, BLOQUEADO ou INATIVO (Domain.Entities.SituacoesCliente). Cliente novo sempre nasce ativo.</summary>
    public string Situacao { get; set; } = Domain.Entities.SituacoesCliente.Ativo;
    [MaxLength(300)] public string? MotivoSituacao { get; set; }
    /// <summary>Cobrança: mensalidade (vazio = não gera mensalidades), vencimento, tolerância e bloqueio automático por atraso.</summary>
    [Range(0, 999999, ErrorMessage = "Valor inválido.")] public decimal? ValorMensalidade { get; set; }
    [Range(1, 28, ErrorMessage = "Dia de vencimento de 1 a 28.")] public byte DiaVencimento { get; set; } = 10;
    [Range(0, 60, ErrorMessage = "Tolerância de 0 a 60 dias.")] public short DiasTolerancia { get; set; } = 5;
    public bool BloqueioPorAtraso { get; set; } = true;
    /// <summary>Período de acesso: cliente novo começa em teste por <see cref="DiasPadraoTeste"/> dias.</summary>
    public bool AcessoDefinitivo { get; set; }
    public DateOnly? AcessoAte { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(DiasPadraoTeste);
    public const int DiasPadraoTeste = 30;

    // Administrador da clínica (só na criação do cliente)
    [MaxLength(150)] public string? AdminNome { get; set; }
    [EmailAddress(ErrorMessage = "E-mail inválido."), MaxLength(254)] public string? AdminEmail { get; set; }
    public string? AdminSenha { get; set; }
    public int? AdminPerfilId { get; set; }
    public List<PerfilOpcaoDto> PerfisPadrao { get; set; } = [];
}

public class NovoUsuarioClienteVM
{
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] public string Nome { get; set; } = "";
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Informe a senha inicial.")] public string Senha { get; set; } = "";
    public List<int> PerfilIds { get; set; } = [];
}

public class AdministradorFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] public string Nome { get; set; } = "";
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] public string Email { get; set; } = "";
    public string? Senha { get; set; }
    public bool Ativo { get; set; } = true;
}

public class ClientesVM
{
    public string? Situacao { get; set; }
    public string? Acesso { get; set; }
    public List<ClienteResumoDto> Itens { get; set; } = [];
}

public class MedicosRelVM
{
    public int? ClinicaId { get; set; }
    public string? Situacao { get; set; }
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public List<MedicoRelDto> Itens { get; set; } = [];
    public List<ClienteResumoDto> Clientes { get; set; } = [];
}

public class UsuariosRelVM
{
    public int? ClinicaId { get; set; }
    public string? Perfil { get; set; }
    public string? Situacao { get; set; }
    public List<UsuarioRelDto> Itens { get; set; } = [];
    public List<ClienteResumoDto> Clientes { get; set; } = [];
    public List<string> Perfis { get; set; } = [];
}

public class AgendamentosRelVM
{
    public int? ClinicaId { get; set; }
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public List<AgendamentoRelDto> Itens { get; set; } = [];
    public List<ClienteResumoDto> Clientes { get; set; } = [];
}

public class PerfisVM
{
    public int? ClinicaId { get; set; }
    public List<PerfilResumoDto> Itens { get; set; } = [];
    public List<ClienteResumoDto> Clientes { get; set; } = [];
}

public class PerfilFormVM
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome."), MaxLength(50)] public string Nome { get; set; } = "";
    [MaxLength(200)] public string? Descricao { get; set; }
    /// <summary>NULL = perfil padrão (todos os clientes).</summary>
    public int? ClinicaId { get; set; }
    public bool Ativo { get; set; } = true;
    public List<string> Transacoes { get; set; } = [];
    public List<ClienteResumoDto> Clientes { get; set; } = [];
}

public class PagamentosVM
{
    public int? ClinicaId { get; set; }
    public string Status { get; set; } = "PENDENTES";
    public DateOnly? De { get; set; }
    public DateOnly? Ate { get; set; }
    public List<CobrancaDto> Itens { get; set; } = [];
    public ResumoCobrancasDto ResumoMes { get; set; } = new(0, 0, 0, 0, 0, 0);
    public List<ClienteResumoDto> Clientes { get; set; } = [];
}

public class NovaCobrancaVM
{
    [MaxLength(100)] public string? Descricao { get; set; }
    public DateOnly? Vencimento { get; set; }
    public decimal? Valor { get; set; }
    /// <summary>true = mensalidade do mês do vencimento (não deixa duplicar o mês).</summary>
    public bool Mensalidade { get; set; }
}

public class PagamentoVM
{
    public CobrancaDto? Cobranca { get; set; }
    public DateOnly PagoEm { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? ValorPago { get; set; }
    public string? FormaPagamento { get; set; } = "PIX";
    [MaxLength(300)] public string? Observacoes { get; set; }
    public string? Voltar { get; set; }
}
