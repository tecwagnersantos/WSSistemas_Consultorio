using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Consultorio.Web.Infra;

public static class Requisicao
{
    /// <summary>Chamadas feitas pelo JavaScript (fetch) enviam o cabeçalho X-Requested-With.</summary>
    public static bool EhAjax(HttpRequest r) =>
        r.Headers.XRequestedWith == "fetch" || r.Headers.Accept.ToString().Contains("application/json");
}

/// <summary>
/// Converte erros de regra de negócio em mensagens para o usuário:
/// JSON { erro } para chamadas JavaScript; mensagem no topo da página (TempData) para formulários.
/// Erros inesperados seguem para a página de erro.
/// </summary>
public class TratamentoErrosFilter(ITempDataDictionaryFactory tempDataFactory, IStringLocalizer<Textos> t) : IExceptionFilter
{
    public void OnException(ExceptionContext ctx)
    {
        var mensagem = MensagemAmigavel(ctx.Exception, t, out var status);
        if (mensagem is null) return;

        var http = ctx.HttpContext;
        if (Requisicao.EhAjax(http.Request))
        {
            ctx.Result = new JsonResult(new { erro = mensagem }) { StatusCode = status };
        }
        else
        {
            tempDataFactory.GetTempData(http)["Erro"] = mensagem;
            var voltar = http.Request.Headers.Referer.ToString();
            ctx.Result = new RedirectResult(string.IsNullOrEmpty(voltar) ? http.Request.PathBase + "/" : voltar);
        }
        ctx.ExceptionHandled = true;
    }

    /// <summary>Mensagem para o usuário, no idioma dele (null = erro inesperado).</summary>
    public static string? MensagemAmigavel(Exception ex, IStringLocalizer t, out int status)
    {
        (status, var msg) = ex switch
        {
            DomainException d => (400, Traduzir(d, t)),
            NaoEncontradoException n => (404, Traduzir(n, t)),
            DbUpdateConcurrencyException => (409, t["O registro foi alterado por outro usuário. Recarregue a página e tente novamente."].Value),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, t["Registro duplicado."].Value),
            DbUpdateException { InnerException: SqlException { Number: 547 } } => (400, t["Operação não permitida: o registro está vinculado a outros dados."].Value),
            DbUpdateException { InnerException: SqlException { Number: 50001 } sql } => (400, t[sql.Message].Value),
            SqlException { Number: 50010 } sql => (409, t[sql.Message].Value),
            _ => (500, (string?)null)
        };
        return msg;
    }

    /// <summary>Traduz a mensagem da regra de negócio: o formato em português é a chave do resource.</summary>
    public static string Traduzir(IMensagemTraduzivel m, IStringLocalizer t) => t[m.Formato, m.Args!].Value;
}
