using Consultorio.Application.Abstractions;
using Consultorio.Application.Relatorios;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Consultorio.Infrastructure.Relatorios;

/// <summary>
/// Relatórios e dashboard com Dapper, lendo as views agenda.vw_AgendaDetalhada e agenda.vw_IndicadoresMensais.
/// O Dapper não passa pelos filtros globais do EF: toda consulta aqui DEVE filtrar por ClinicaId = @clinicaId.
/// </summary>
public class RelatorioQueries(IConfiguration config, IUsuarioAtual usuario) : IRelatorioQueries
{
    private SqlConnection Conexao() => new(config.GetConnectionString("Consultorio"));

    private int Clinica => usuario.ClinicaId ?? throw new InvalidOperationException("Relatório sem clínica definida.");

    public async Task<DashboardDto> DashboardAsync(int? medicoId, CancellationToken ct)
    {
        const string sql = """
            DECLARE @hoje DATE = CAST(SYSDATETIME() AS DATE);
            DECLARE @iniMes DATE = DATEFROMPARTS(YEAR(@hoje), MONTH(@hoje), 1);

            SELECT
                SUM(CASE WHEN Data = @hoje AND Status NOT IN ('CANCELADO','REMARCADO') THEN 1 ELSE 0 END) AS AgendadosHoje,
                SUM(CASE WHEN Data = @hoje AND Status = 'ATENDIDO' THEN 1 ELSE 0 END)                     AS AtendidosHoje,
                SUM(CASE WHEN Data = @hoje AND Status = 'AGUARDANDO' THEN 1 ELSE 0 END)                   AS AguardandoAgora,
                SUM(CASE WHEN Data >= @iniMes AND Status = 'FALTOU' THEN 1 ELSE 0 END)                    AS FaltasMes,
                SUM(CASE WHEN Data >= @iniMes AND Status = 'ATENDIDO' THEN 1 ELSE 0 END)                  AS AtendidosMes,
                ISNULL(SUM(CASE WHEN Data >= @iniMes AND Status = 'ATENDIDO' THEN Valor END), 0)          AS ValorAtendidoMes,
                AVG(CASE WHEN Data >= @iniMes THEN CAST(EsperaMin AS FLOAT) END)                          AS EsperaMediaMinMes
            FROM agenda.vw_AgendaDetalhada
            WHERE ClinicaId = @clinicaId AND Data >= DATEADD(DAY, -31, @hoje) AND (@medicoId IS NULL OR MedicoId = @medicoId);

            SELECT CAST(Data AS DATETIME2) AS Data,
                   SUM(CASE WHEN Status = 'ATENDIDO'  THEN 1 ELSE 0 END) AS Atendidos,
                   SUM(CASE WHEN Status = 'FALTOU'    THEN 1 ELSE 0 END) AS Faltas,
                   SUM(CASE WHEN Status = 'CANCELADO' THEN 1 ELSE 0 END) AS Cancelados
            FROM agenda.vw_AgendaDetalhada
            WHERE ClinicaId = @clinicaId AND Data > DATEADD(DAY, -30, @hoje) AND Data <= @hoje AND (@medicoId IS NULL OR MedicoId = @medicoId)
            GROUP BY Data ORDER BY Data;

            SELECT ISNULL(Convenio, N'Particular') AS Nome, COUNT(*) AS Quantidade, ISNULL(SUM(Valor), 0) AS Valor
            FROM agenda.vw_AgendaDetalhada
            WHERE ClinicaId = @clinicaId AND Data >= @iniMes AND Status = 'ATENDIDO' AND (@medicoId IS NULL OR MedicoId = @medicoId)
            GROUP BY ISNULL(Convenio, N'Particular') ORDER BY Quantidade DESC;
            """;

        await using var cn = Conexao();
        using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { clinicaId = Clinica, medicoId }, cancellationToken: ct));
        var k = await multi.ReadSingleAsync<Kpis>();
        var serie = (await multi.ReadAsync<SerieDiaDto>()).ToList();
        var convenios = (await multi.ReadAsync<ItemContagemDto>()).ToList();

        var baseFaltas = (k.AtendidosMes ?? 0) + (k.FaltasMes ?? 0);
        var taxa = baseFaltas == 0 ? 0 : Math.Round((decimal)(k.FaltasMes ?? 0) * 100 / baseFaltas, 1);
        return new DashboardDto(k.AgendadosHoje ?? 0, k.AtendidosHoje ?? 0, k.AguardandoAgora ?? 0, k.FaltasMes ?? 0,
            k.AtendidosMes ?? 0, k.ValorAtendidoMes, taxa, k.EsperaMediaMinMes, serie, convenios);
    }

    public async Task<IReadOnlyList<IndicadorMensalDto>> IndicadoresMensaisAsync(DateOnly de, DateOnly ate, int? medicoId, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(i.Mes AS DATETIME2) AS Mes, i.MedicoId, m.Nome AS Medico, i.TotalAgendamentos, i.Atendidos,
                   i.Faltas, i.Cancelados, i.Particulares, i.ValorAtendido
            FROM agenda.vw_IndicadoresMensais i
            JOIN cadastro.Medico m ON m.Id = i.MedicoId AND m.ClinicaId = i.ClinicaId
            WHERE i.ClinicaId = @clinicaId AND i.Mes BETWEEN @de AND @ate AND (@medicoId IS NULL OR i.MedicoId = @medicoId)
            ORDER BY i.Mes, m.Nome;
            """;
        await using var cn = Conexao();
        var r = await cn.QueryAsync<IndicadorMensalDto>(new CommandDefinition(sql,
            new { clinicaId = Clinica, de = de.ToDateTime(TimeOnly.MinValue), ate = ate.ToDateTime(TimeOnly.MinValue), medicoId }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<IReadOnlyList<AtendimentoRelatorioDto>> AtendimentosAsync(DateOnly de, DateOnly ate, int? medicoId, string? status, CancellationToken ct)
    {
        const string sql = """
            SELECT Id, Inicio, Status, TipoAtendimento, Medico, Especialidade, Paciente, Convenio, Plano, Valor, EsperaMin
            FROM agenda.vw_AgendaDetalhada
            WHERE ClinicaId = @clinicaId AND Data BETWEEN @de AND @ate
              AND (@medicoId IS NULL OR MedicoId = @medicoId)
              AND (@status IS NULL OR Status = @status)
            ORDER BY Inicio;
            """;
        await using var cn = Conexao();
        var r = await cn.QueryAsync<AtendimentoRelatorioDto>(new CommandDefinition(sql,
            new { clinicaId = Clinica, de = de.ToDateTime(TimeOnly.MinValue), ate = ate.ToDateTime(TimeOnly.MinValue), medicoId, status }, cancellationToken: ct));
        return r.ToList();
    }

    private sealed class Kpis
    {
        public int? AgendadosHoje { get; set; }
        public int? AtendidosHoje { get; set; }
        public int? AguardandoAgora { get; set; }
        public int? FaltasMes { get; set; }
        public int? AtendidosMes { get; set; }
        public decimal ValorAtendidoMes { get; set; }
        public double? EsperaMediaMinMes { get; set; }
    }
}
