using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Consultorio.Web.Infra;

/// <summary>
/// Autenticação separada da área /Admin (administradores do sistema): cookie, tela de login e tabela próprios.
/// Um login de clínica não abre a área admin, e o login admin não abre as telas das clínicas.
/// </summary>
public static class AdminAuth
{
    public const string Esquema = "Admin";
    public const string Politica = "AdministradorSistema";
    public const string Area = "Admin";
}

/// <summary>
/// Exige login de clínica (cookie padrão) em todos os controllers fora da área Admin.
/// A área Admin usa a política <see cref="AdminAuth.Politica"/>, que só aceita o cookie da área.
/// </summary>
public class LoginDaClinicaConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (controller.RouteValues.TryGetValue("area", out var area) && area == AdminAuth.Area) return;
        controller.Filters.Add(new AuthorizeFilter());
    }
}
