using Microsoft.AspNetCore.Localization;

namespace Consultorio.Web.Infra;

/// <summary>Cookie de idioma (".AspNetCore.Culture") lido pelo UseRequestLocalization em cada requisição.</summary>
public static class IdiomaCookie
{
    public static void Definir(HttpResponse resposta, string idioma) =>
        resposta.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(idioma)),
            new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
}
