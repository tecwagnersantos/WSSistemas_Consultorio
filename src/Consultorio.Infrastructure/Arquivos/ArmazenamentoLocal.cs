using Consultorio.Application.Abstractions;

namespace Consultorio.Infrastructure.Arquivos;

/// <summary>
/// Anexos em pasta do servidor (fora do wwwroot): {raiz}/{clinicaId}/{ano}/{mês}/{guid}.{ext}.
/// O nome original fica só no banco; no disco o nome é gerado, então não há como montar caminho a partir do que o usuário envia.
/// A conta do Application Pool do IIS precisa de permissão de Modificar na pasta raiz.
/// </summary>
public class ArmazenamentoLocal(string raiz) : IArmazenamentoArquivos
{
    private readonly string raiz = Path.GetFullPath(raiz);

    public string Raiz => raiz;

    public async Task<string> SalvarAsync(int clinicaId, string extensao, Func<Stream, Task> escrever, CancellationToken ct)
    {
        var agora = DateTime.Now;
        var relativo = $"{clinicaId}/{agora:yyyy}/{agora:MM}/{Guid.NewGuid():N}{extensao}";
        var completo = Completo(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        try
        {
            await using var fs = new FileStream(completo, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await escrever(fs);
        }
        catch
        {
            File.Delete(completo);
            throw;
        }
        return relativo;
    }

    public Stream Abrir(string caminho) =>
        new FileStream(Completo(caminho), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Excluir(string caminho)
    {
        try { File.Delete(Completo(caminho)); } catch (IOException) { }
    }

    private string Completo(string relativo)
    {
        var completo = Path.GetFullPath(Path.Combine(raiz, relativo));
        if (!completo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de anexo fora da pasta de anexos.");
        return completo;
    }
}
