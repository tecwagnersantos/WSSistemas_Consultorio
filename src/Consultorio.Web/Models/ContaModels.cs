using System.ComponentModel.DataAnnotations;

namespace Consultorio.Web.Models;

public class LoginVM
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe a senha.")]
    public string Senha { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class AlterarSenhaVM
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    public string SenhaAtual { get; set; } = "";

    [Required(ErrorMessage = "Informe a nova senha.")]
    public string NovaSenha { get; set; } = "";

    [Compare(nameof(NovaSenha), ErrorMessage = "A confirmação não confere com a nova senha.")]
    public string Confirmacao { get; set; } = "";
}

public class DemonstracaoVM
{
    [Required(ErrorMessage = "Informe o nome da clínica."), MaxLength(150)] public string NomeClinica { get; set; } = "";
    [MaxLength(20)] public string? Telefone { get; set; }
    [MaxLength(100)] public string? Cidade { get; set; }
    [MaxLength(2)] public string? Uf { get; set; }

    [Required(ErrorMessage = "Informe o seu nome."), MaxLength(150)] public string NomeResponsavel { get; set; } = "";
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido."), MaxLength(254)] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Informe a senha.")] public string Senha { get; set; } = "";
    [Required(ErrorMessage = "Confirme a senha."), Compare(nameof(Senha), ErrorMessage = "A confirmação não confere com a senha.")]
    public string Confirmacao { get; set; } = "";

    public bool SouMedico { get; set; } = true;
    [MaxLength(10)] public string? Crm { get; set; }
    [MaxLength(2)] public string? UfCrm { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "É preciso aceitar os termos da demonstração.")]
    public bool AceitoTermos { get; set; }
}
