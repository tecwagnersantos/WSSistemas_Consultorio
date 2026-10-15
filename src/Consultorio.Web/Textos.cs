using System.Globalization;

namespace Consultorio.Web;

/// <summary>
/// Marcador dos textos traduzidos (Resources/Textos.resx = português, padrão; Textos.en/.es/.fr.resx).
/// A chave é o próprio texto em português: o que faltar na tradução aparece em português.
/// Nas views: @T["Texto"]; nos controllers: T["Texto"] (BaseController).
/// </summary>
public class Textos;

/// <summary>Idiomas oferecidos no sistema (seletor no topo das páginas, nas telas de login e na área /Admin).</summary>
public static class Idiomas
{
    public const string Padrao = "pt-BR";

    public static readonly IReadOnlyList<(string Codigo, string Nome)> Todos =
    [
        ("pt-BR", "Português"), ("en-US", "English"), ("es-ES", "Español"), ("fr-FR", "Français")
    ];

    public static CultureInfo[] Culturas => Todos.Select(i => new CultureInfo(i.Codigo)).ToArray();

    /// <summary>Código suportado ou null (aceita "en", "EN-us" etc.).</summary>
    public static string? Normalizar(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        return Todos.Select(i => i.Codigo).FirstOrDefault(c => c.Equals(codigo, StringComparison.OrdinalIgnoreCase))
               ?? Todos.Select(i => i.Codigo).FirstOrDefault(c => c[..2].Equals(codigo.Length >= 2 ? codigo[..2] : codigo, StringComparison.OrdinalIgnoreCase));
    }
}
