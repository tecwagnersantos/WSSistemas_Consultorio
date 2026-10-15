using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Consultorio.Web.Infra;

/// <summary>
/// Lê os campos de formulário com cultura invariante: input type="number" envia "36.5" e
/// input type="date" envia "2026-10-07", mesmo com a aplicação exibindo tudo em pt-BR.
/// </summary>
public class FormularioInvarianteValueProviderFactory : IValueProviderFactory
{
    public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        var request = context.ActionContext.HttpContext.Request;
        if (!request.HasFormContentType) return;
        var form = await request.ReadFormAsync();
        context.ValueProviders.Add(new FormValueProvider(BindingSource.Form, form, CultureInfo.InvariantCulture));
    }
}
