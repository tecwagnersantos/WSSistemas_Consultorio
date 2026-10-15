using Consultorio.Domain.Common;

namespace Consultorio.Domain.Entities;

/// <summary>
/// Cobrança feita a um cliente (mensalidade ou avulsa, ex.: implantação) e o pagamento dela.
/// Mantida só pela área /Admin. Mensalidade em atraso além da tolerância bloqueia o cliente
/// quando <see cref="Clinica.BloqueioPorAtraso"/> está ligado.
/// </summary>
public class Cobranca : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public string Descricao { get; set; } = "";
    /// <summary>Primeiro dia do mês da mensalidade; NULL em cobrança avulsa.</summary>
    public DateOnly? Competencia { get; set; }
    public DateOnly Vencimento { get; set; }
    public decimal Valor { get; set; }
    public DateOnly? PagoEm { get; set; }
    public decimal? ValorPago { get; set; }
    public string? FormaPagamento { get; set; }
    public string? Observacoes { get; set; }
    public bool Cancelada { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public bool Paga => PagoEm is not null;
    public string Status(DateOnly hoje) => Cancelada ? StatusCobranca.Cancelada : Paga ? StatusCobranca.Paga
        : Vencimento < hoje ? StatusCobranca.Atrasada : StatusCobranca.Aberta;
}

public static class StatusCobranca
{
    public const string Aberta = "ABERTA";
    public const string Atrasada = "ATRASADA";
    public const string Paga = "PAGA";
    public const string Cancelada = "CANCELADA";

    public static string Nome(string s) => s switch
    {
        Aberta => "Em aberto", Atrasada => "Atrasada", Paga => "Paga", Cancelada => "Cancelada", _ => s
    };
}

public static class FormasPagamento
{
    public static readonly IReadOnlyList<(string Codigo, string Nome)> Todas =
    [
        ("PIX", "Pix"), ("BOLETO", "Boleto"), ("CARTAO", "Cartão"), ("TRANSFERENCIA", "Transferência"), ("DINHEIRO", "Dinheiro"), ("OUTRO", "Outro")
    ];

    public static string Nome(string? codigo) => Todas.FirstOrDefault(f => f.Codigo == codigo).Nome ?? codigo ?? "";
}
