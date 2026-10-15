using System.Globalization;

namespace Consultorio.Domain.Common;

/// <summary>
/// Mensagem de erro com texto em português que a tela traduz para o idioma do usuário (Resources/Textos.*.resx).
/// <see cref="Formato"/> é a chave do resource (pode ter {0}, {1}...) e <see cref="Args"/> os valores.
/// </summary>
public interface IMensagemTraduzivel
{
    string Formato { get; }
    object?[] Args { get; }
}

/// <summary>Violação de regra de negócio. A tela mostra a mensagem traduzida; JSON devolve 400/409.</summary>
public class DomainException(string formato, params object?[] args)
    : Exception(MensagemTraduzivel.Formatar(formato, args)), IMensagemTraduzivel
{
    public string Formato { get; } = formato;
    public object?[] Args { get; } = args;
}

public static class MensagemTraduzivel
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>Texto em português (Message da exceção, logs).</summary>
    public static string Formatar(string formato, object?[] args) => args.Length == 0 ? formato : string.Format(PtBr, formato, args);
}
