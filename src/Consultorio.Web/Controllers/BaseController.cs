using System.Text;
using Consultorio.Application.Abstractions;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Consultorio.Web.Controllers;

public abstract class BaseController : Controller
{
    protected IUsuarioAtual UsuarioAtual => HttpContext.RequestServices.GetRequiredService<IUsuarioAtual>();

    /// <summary>Textos no idioma do usuário (Resources/Textos*.resx). A chave é o texto em português.</summary>
    protected IStringLocalizer T => HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Textos>>();

    /// <summary>Mensagem verde exibida no topo da próxima página (já traduzida: passe T["..."]).</summary>
    /// <summary>Mensagem de sucesso no topo da próxima página, traduzida (o texto é a chave em português).</summary>
    protected void Sucesso(string formato, params object?[] args) => TempData["Sucesso"] = T[formato, args!].Value;

    /// <summary>Mensagem vermelha no topo da próxima página com os erros do ModelState.</summary>
    protected void ErroNoTopo() => TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

    /// <summary>
    /// Executa a gravação; se uma regra de negócio for violada, coloca a mensagem no ModelState
    /// (para reexibir o formulário com os dados digitados) e devolve false.
    /// </summary>
    protected async Task<bool> TentarAsync(Func<Task> acao)
    {
        try
        {
            await acao();
            return true;
        }
        catch (Exception ex) when (TratamentoErrosFilter.MensagemAmigavel(ex, T, out _) is not null)
        {
            ModelState.AddModelError("", TratamentoErrosFilter.MensagemAmigavel(ex, T, out _)!);
            return false;
        }
    }

    /// <summary>Resposta JSON padrão para chamadas JavaScript.</summary>
    protected IActionResult JsonOk(object? dados = null) => Json(dados ?? new { ok = true });

    /// <summary>
    /// CSV com BOM UTF-8 no formato do idioma (separador de lista e decimais): abre direto no Excel.
    /// Cabeçalhos são traduzidos aqui; passe-os em português.
    /// </summary>
    protected FileContentResult Csv(string nome, string[] cabecalho, IEnumerable<object?[]> linhas)
    {
        var cultura = System.Globalization.CultureInfo.CurrentCulture;
        var sep = cultura.TextInfo.ListSeparator is { Length: 1 } ls ? ls[0] : ';';
        string sim = T["Sim"], nao = T["Não"];
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, cabecalho.Select(c => Escapar(T[c]))));
        foreach (var l in linhas)
            sb.AppendLine(string.Join(sep, l.Select(v => Escapar(v switch
            {
                null => "",
                decimal d => d.ToString("0.00", cultura),
                bool b => b ? sim : nao,
                IFormattable f => f.ToString(null, cultura),
                _ => v.ToString() ?? ""
            }))));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", nome);

        string Escapar(string s) => s.IndexOfAny([sep, '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
