namespace Consultorio.Application.Common;

public record PaginaResultado<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int TamanhoPagina);

public class NaoEncontradoException(string formato, params object?[] args)
    : Exception(Domain.Common.MensagemTraduzivel.Formatar(formato, args)), Domain.Common.IMensagemTraduzivel
{
    public string Formato { get; } = formato;
    public object?[] Args { get; } = args;
}
