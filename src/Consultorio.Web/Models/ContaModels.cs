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
