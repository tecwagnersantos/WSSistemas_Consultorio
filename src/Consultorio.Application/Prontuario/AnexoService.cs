using System.Linq.Expressions;
using System.Security.Cryptography;
using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Prontuario;

public record AnexoDto(int Id, int PacienteId, int? EvolucaoId, string Tipo, string? Descricao, string NomeArquivo,
    string ContentType, long TamanhoBytes, DateTime EnviadoEm, string? EnviadoPor, int? EnviadoPorUsuarioId)
{
    public bool EhImagem => ContentType.StartsWith("image/") && ContentType is not "image/tiff" and not "image/heic";
    /// <summary>Imagens comuns e PDF abrem no navegador; os demais formatos são baixados.</summary>
    public bool AbreNoNavegador => EhImagem || ContentType == "application/pdf";
}

public record AnexoEnvioRequest(string Tipo, string? Descricao, int? EvolucaoId);
public record ArquivoEnviado(string NomeArquivo, Stream Conteudo);

/// <summary>Limites dos anexos (appsettings: Anexos:TamanhoMaximoMB).</summary>
public record OpcoesAnexos(int TamanhoMaximoMB = 20, int MaximoArquivosPorEnvio = 10)
{
    public long TamanhoMaximoBytes => TamanhoMaximoMB * 1024L * 1024L;
}

/// <summary>
/// Anexos do prontuário (imagens, exames, laudos, termos...). Mesmas regras do prontuário: só o perfil Médico,
/// isolado por clínica (filtro global + pasta por clínica) e toda leitura e gravação auditada.
/// </summary>
public class AnexoService(IAppDbContext db, IUsuarioAtual usuario, IAuditoria auditoria, IArmazenamentoArquivos armazenamento,
    OpcoesAnexos opcoes)
{
    /// <summary>Formatos aceitos. O tipo de conteúdo vem da extensão, nunca do navegador.</summary>
    public static readonly IReadOnlyDictionary<string, string> Formatos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".tif"] = "image/tiff", [".tiff"] = "image/tiff", [".heic"] = "image/heic",
        [".dcm"] = "application/dicom",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".txt"] = "text/plain"
    };

    public async Task<List<AnexoDto>> ListarAsync(int pacienteId, CancellationToken ct)
    {
        var lista = await Consulta(a => a.PacienteId == pacienteId).ToListAsync(ct);
        auditoria.Registrar("LEITURA", "prontuario.Anexo", $"paciente:{pacienteId}");
        await db.SaveChangesAsync(ct);
        return lista;
    }

    public async Task<List<int>> EnviarAsync(int pacienteId, AnexoEnvioRequest req, IReadOnlyList<ArquivoEnviado> arquivos, CancellationToken ct)
    {
        var clinicaId = usuario.ClinicaId ?? throw new DomainException("Entre com um usuário da clínica para anexar arquivos.");
        if (arquivos.Count == 0) throw new DomainException("Selecione ao menos um arquivo.");
        if (arquivos.Count > opcoes.MaximoArquivosPorEnvio) throw new DomainException("Envie no máximo {0} arquivos por vez.", opcoes.MaximoArquivosPorEnvio);
        if (!TiposAnexo.Todos.Contains(req.Tipo)) throw new DomainException("Tipo de anexo inválido.");
        if (req.Descricao?.Length > 200) throw new DomainException("Descrição com no máximo 200 caracteres.");
        if (!await db.Pacientes.AnyAsync(p => p.Id == pacienteId, ct)) throw new NaoEncontradoException("Paciente não encontrado.");
        if (req.EvolucaoId is int evo && !await db.Evolucoes.AnyAsync(e => e.Id == evo && e.PacienteId == pacienteId, ct))
            throw new DomainException("Evolução não pertence ao paciente.");
        foreach (var a in arquivos)
            if (!Formatos.ContainsKey(Path.GetExtension(a.NomeArquivo)))
                throw new DomainException("Formato não aceito: {0}. Aceitos: {1}.", NomeSeguro(a.NomeArquivo), string.Join(", ", Formatos.Keys.Select(k => k.TrimStart('.'))));

        var gravados = new List<string>();
        try
        {
            var novos = new List<Anexo>();
            foreach (var a in arquivos)
            {
                var ext = Path.GetExtension(a.NomeArquivo).ToLowerInvariant();
                var (caminho, tamanho, hash) = await GravarAsync(clinicaId, ext, a, ct);
                gravados.Add(caminho);
                novos.Add(new Anexo
                {
                    PacienteId = pacienteId, EvolucaoId = req.EvolucaoId, Tipo = req.Tipo,
                    Descricao = string.IsNullOrWhiteSpace(req.Descricao) ? null : req.Descricao.Trim(),
                    NomeArquivo = NomeSeguro(a.NomeArquivo), ContentType = Formatos[ext], TamanhoBytes = tamanho,
                    CaminhoArmazenamento = caminho, HashSha256 = hash, EnviadoPorUsuarioId = usuario.UsuarioId
                });
            }
            db.Anexos.AddRange(novos);
            await db.SaveChangesAsync(ct);
            foreach (var n in novos)
                auditoria.Registrar("INCLUSAO", "prontuario.Anexo", n.Id, novo: new { n.PacienteId, n.Tipo, n.NomeArquivo, n.TamanhoBytes });
            await db.SaveChangesAsync(ct);
            return novos.Select(n => n.Id).ToList();
        }
        catch
        {
            foreach (var c in gravados) armazenamento.Excluir(c);
            throw;
        }
    }

    /// <summary>Abre o arquivo para visualizar ou baixar (auditado). Confere o SHA-256 gravado no envio.</summary>
    public async Task<(AnexoDto Anexo, Stream Conteudo)> AbrirAsync(int pacienteId, int anexoId, CancellationToken ct)
    {
        var a = await db.Anexos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == anexoId && x.PacienteId == pacienteId && x.Ativo, ct)
                ?? throw new NaoEncontradoException("Anexo não encontrado.");
        var dto = await Consulta(x => x.Id == anexoId).FirstAsync(ct);
        Stream conteudo;
        try { conteudo = armazenamento.Abrir(a.CaminhoArmazenamento); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DomainException("O arquivo deste anexo não foi encontrado na pasta de anexos do servidor. Avise o suporte.");
        }
        auditoria.Registrar("LEITURA", "prontuario.Anexo", anexoId);
        await db.SaveChangesAsync(ct);
        return (dto, conteudo);
    }

    /// <summary>Tira o anexo da lista (o arquivo e o registro continuam guardados). Só quem enviou pode remover.</summary>
    public async Task RemoverAsync(int pacienteId, int anexoId, CancellationToken ct)
    {
        var a = await db.Anexos.FirstOrDefaultAsync(x => x.Id == anexoId && x.PacienteId == pacienteId && x.Ativo, ct)
                ?? throw new NaoEncontradoException("Anexo não encontrado.");
        if (a.EnviadoPorUsuarioId != usuario.UsuarioId) throw new DomainException("Somente quem enviou o anexo pode removê-lo.");
        a.Ativo = false;
        auditoria.Registrar("EXCLUSAO", "prontuario.Anexo", a.Id, new { a.NomeArquivo, a.Tipo }, new { a.Ativo });
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<AnexoDto> Consulta(Expression<Func<Anexo, bool>> filtro) =>
        from a in db.Anexos.AsNoTracking().Where(filtro)
        where a.Ativo
        orderby a.EnviadoEm descending, a.Id descending
        select new AnexoDto(a.Id, a.PacienteId, a.EvolucaoId, a.Tipo, a.Descricao, a.NomeArquivo, a.ContentType, a.TamanhoBytes,
            a.EnviadoEm, db.Usuarios.Where(u => u.Id == a.EnviadoPorUsuarioId).Select(u => u.Nome).FirstOrDefault(), a.EnviadoPorUsuarioId);

    /// <summary>Grava o arquivo conferindo o tamanho real (não o informado) e a assinatura dos formatos mais comuns.</summary>
    private async Task<(string Caminho, long Tamanho, byte[] Hash)> GravarAsync(int clinicaId, string ext, ArquivoEnviado a, CancellationToken ct)
    {
        var cabecalho = new byte[12];
        var lidos = await a.Conteudo.ReadAtLeastAsync(cabecalho, cabecalho.Length, throwOnEndOfStream: false, ct);
        if (lidos == 0) throw new DomainException("O arquivo {0} está vazio.", NomeSeguro(a.NomeArquivo));
        if (!AssinaturaConfere(ext, cabecalho.AsSpan(0, lidos)))
            throw new DomainException("O conteúdo de {0} não corresponde à extensão {1}.", NomeSeguro(a.NomeArquivo), ext);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        var caminho = await armazenamento.SalvarAsync(clinicaId, ext, async destino =>
        {
            sha.AppendData(cabecalho, 0, lidos);
            await destino.WriteAsync(cabecalho.AsMemory(0, lidos), ct);
            total = lidos;
            var buffer = new byte[81920];
            int n;
            while ((n = await a.Conteudo.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > opcoes.TamanhoMaximoBytes)
                    throw new DomainException("O arquivo {0} passa de {1} MB.", NomeSeguro(a.NomeArquivo), opcoes.TamanhoMaximoMB);
                sha.AppendData(buffer, 0, n);
                await destino.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }, ct);
        return (caminho, total, sha.GetHashAndReset());
    }

    private static bool AssinaturaConfere(string ext, ReadOnlySpan<byte> h) => ext switch
    {
        ".pdf" => h.StartsWith("%PDF"u8),
        ".png" => h.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        ".jpg" or ".jpeg" => h.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".gif" => h.StartsWith("GIF8"u8),
        ".webp" => h.Length >= 12 && h.StartsWith("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8),
        ".bmp" => h.StartsWith("BM"u8),
        _ => true
    };

    /// <summary>Só o nome (sem pasta), sem caracteres de controle e com no máximo 255 caracteres.</summary>
    private static string NomeSeguro(string nome)
    {
        var n = new string(Path.GetFileName(nome.Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length == 0) n = "arquivo";
        return n.Length <= 255 ? n : n[..(255 - Path.GetExtension(n).Length)] + Path.GetExtension(n);
    }
}
