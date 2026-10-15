using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Consultorio.Web.Infra;

/// <summary>
/// Dá mensagem em português (chave de Resources/Textos*.resx) aos atributos de validação declarados sem ErrorMessage,
/// para que passem pela tradução em vez de mostrar o texto padrão do .NET em inglês.
/// </summary>
public sealed class MensagensValidacao : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var a in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (a.ErrorMessage != null || a.ErrorMessageResourceName != null) continue;
            a.ErrorMessage = a switch
            {
                RequiredAttribute => "Campo obrigatório.",
                MaxLengthAttribute => "Máximo de {1} caracteres.",
                StringLengthAttribute => "Máximo de {1} caracteres.",
                MinLengthAttribute => "Mínimo de {1} caracteres.",
                EmailAddressAttribute => "E-mail inválido.",
                RangeAttribute => "Informe um valor entre {1} e {2}.",
                RegularExpressionAttribute => "Formato inválido.",
                _ => null
            };
        }
    }
}
