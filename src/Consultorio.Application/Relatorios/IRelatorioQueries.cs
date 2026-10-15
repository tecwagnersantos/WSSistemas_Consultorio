namespace Consultorio.Application.Relatorios;

public record DashboardDto(int AgendadosHoje, int AtendidosHoje, int AguardandoAgora, int FaltasMes,
    int AtendidosMes, decimal ValorAtendidoMes, decimal TaxaFaltasMes, double? EsperaMediaMinMes,
    IReadOnlyList<SerieDiaDto> AtendimentosUltimos30Dias, IReadOnlyList<ItemContagemDto> PorConvenioMes);
public record SerieDiaDto(DateTime Data, int Atendidos, int Faltas, int Cancelados);
public record ItemContagemDto(string Nome, int Quantidade, decimal Valor);
public record IndicadorMensalDto(DateTime Mes, int MedicoId, string Medico, int TotalAgendamentos, int Atendidos,
    int Faltas, int Cancelados, int Particulares, decimal ValorAtendido);
public record AtendimentoRelatorioDto(int Id, DateTime Inicio, string Status, string TipoAtendimento, string Medico,
    string Especialidade, string Paciente, string? Convenio, string? Plano, decimal? Valor, int? EsperaMin);

/// <summary>Consultas de leitura pesadas (Dapper sobre as views do banco).</summary>
public interface IRelatorioQueries
{
    Task<DashboardDto> DashboardAsync(int? medicoId, CancellationToken ct);
    Task<IReadOnlyList<IndicadorMensalDto>> IndicadoresMensaisAsync(DateOnly de, DateOnly ate, int? medicoId, CancellationToken ct);
    Task<IReadOnlyList<AtendimentoRelatorioDto>> AtendimentosAsync(DateOnly de, DateOnly ate, int? medicoId, string? status, CancellationToken ct);
}
