using System.Security.Cryptography;
using System.Text;
using Consultorio.Domain.Common;

namespace Consultorio.Domain.Entities;

public class ResumoClinico : IPertenceClinica
{
    public int PacienteId { get; set; }
    public int ClinicaId { get; set; }
    public string? TipoSanguineo { get; set; }
    public string? Alergias { get; set; }
    public string? Comorbidades { get; set; }
    public string? MedicamentosEmUso { get; set; }
    public string? AntecedentesPessoais { get; set; }
    public string? AntecedentesFamiliares { get; set; }
    public string? Habitos { get; set; }
    public int? AtualizadoPorUsuarioId { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.Now;
}

public class Evolucao : IPertenceClinica
{
    public const string Rascunho = "R";
    public const string Assinada = "A";

    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int PacienteId { get; set; }
    public int MedicoId { get; set; }
    public int? AgendamentoId { get; set; }
    public int? EvolucaoRetificadaId { get; set; }
    public DateTime DataHora { get; set; } = DateTime.Now;
    public string? QueixaPrincipal { get; set; }
    public string? HistoriaDoencaAtual { get; set; }
    public string? ExameFisico { get; set; }
    public string? HipoteseDiagnostica { get; set; }
    public string? Conduta { get; set; }
    public decimal? PesoKg { get; set; }
    public decimal? AlturaCm { get; set; }
    public short? PressaoSistolica { get; set; }
    public short? PressaoDiastolica { get; set; }
    public short? FrequenciaCardiaca { get; set; }
    public decimal? TemperaturaC { get; set; }
    public byte? SaturacaoO2 { get; set; }
    public string Situacao { get; private set; } = Rascunho;
    public DateTime? AssinadaEm { get; private set; }
    public byte[]? HashConteudo { get; private set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public Medico Medico { get; set; } = null!;

    public bool EstaAssinada => Situacao == Assinada;

    public void GarantirEditavel()
    {
        if (EstaAssinada)
            throw new DomainException("Evolução assinada não pode ser alterada. Registre uma retificação.");
    }

    /// <summary>Assina a evolução: congela o conteúdo e grava o SHA-256 dele.</summary>
    public void Assinar()
    {
        GarantirEditavel();
        AssinadaEm = DateTime.Now;
        Situacao = Assinada;
        HashConteudo = SHA256.HashData(Encoding.UTF8.GetBytes(ConteudoCanonico()));
    }

    private string ConteudoCanonico() => string.Join('\u001f',
        PacienteId, MedicoId, DataHora.ToString("O"), QueixaPrincipal, HistoriaDoencaAtual, ExameFisico,
        HipoteseDiagnostica, Conduta, PesoKg, AlturaCm, PressaoSistolica, PressaoDiastolica,
        FrequenciaCardiaca, TemperaturaC, SaturacaoO2, AssinadaEm?.ToString("O"));
}

public class Prescricao : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int? EvolucaoId { get; set; }
    public int PacienteId { get; set; }
    public int MedicoId { get; set; }
    public string Tipo { get; set; } = "S";
    public DateTime EmitidaEm { get; set; } = DateTime.Now;
    public short? ValidadeDias { get; set; }
    public string? Observacoes { get; set; }
    public DateTime? AssinadaEm { get; set; }
    public ICollection<PrescricaoItem> Itens { get; set; } = [];
}

public class PrescricaoItem
{
    public int Id { get; set; }
    public int PrescricaoId { get; set; }
    public byte Ordem { get; set; }
    public string Medicamento { get; set; } = "";
    public string? Concentracao { get; set; }
    public string? FormaFarmaceutica { get; set; }
    public string? ViaAdministracao { get; set; }
    public string Posologia { get; set; } = "";
    public string? Quantidade { get; set; }
    public bool UsoContinuo { get; set; }
}

/// <summary>
/// Arquivo anexado ao prontuário (exame, imagem, laudo, termo...). O conteúdo fica em disco, numa pasta por clínica;
/// o banco guarda os metadados e o SHA-256. Anexo não é apagado: "remover" só o tira da lista (Ativo = false).
/// </summary>
public class Anexo : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int PacienteId { get; set; }
    public int? EvolucaoId { get; set; }
    public string Tipo { get; set; } = TiposAnexo.Documento;
    public string? Descricao { get; set; }
    public string NomeArquivo { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long TamanhoBytes { get; set; }
    public string CaminhoArmazenamento { get; set; } = "";
    public byte[] HashSha256 { get; set; } = [];
    public int? EnviadoPorUsuarioId { get; set; }
    public DateTime EnviadoEm { get; set; } = DateTime.Now;
    public bool Ativo { get; set; } = true;
}

public static class TiposAnexo
{
    public const string Exame = "EXAME";
    public const string Imagem = "IMAGEM";
    public const string Laudo = "LAUDO";
    public const string Termo = "TERMO";
    public const string Documento = "DOCUMENTO";
    public const string Outro = "OUTRO";
    public static readonly string[] Todos = [Exame, Imagem, Laudo, Termo, Documento, Outro];

    public static string Nome(string tipo) => tipo switch
    {
        Exame => "Exame", Imagem => "Imagem", Laudo => "Laudo", Termo => "Termo / consentimento", Documento => "Documento", _ => "Outro"
    };
}
