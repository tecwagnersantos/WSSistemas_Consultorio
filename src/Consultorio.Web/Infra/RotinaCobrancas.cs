using Consultorio.Application.Admin;

namespace Consultorio.Web.Infra;

/// <summary>
/// Roda ao iniciar o site e de hora em hora: gera as mensalidades do mês e aplica a regra de atraso
/// (bloqueia quem passou da tolerância, reativa quem pagou). No IIS o site pode estar parado de madrugada;
/// por isso o login da clínica também confere o atraso dela antes de entrar.
/// </summary>
public class RotinaCobrancas(IServiceScopeFactory scopes, ILogger<RotinaCobrancas> log) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Deixa o site terminar de subir antes da primeira rodada
        try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var (geradas, alterados) = await scope.ServiceProvider.GetRequiredService<CobrancaService>().ProcessarAsync(ct);
                if (geradas > 0 || alterados > 0)
                    log.LogInformation("Rotina de pagamentos: {Geradas} mensalidade(s) gerada(s), {Alterados} cliente(s) bloqueado(s)/reativado(s).", geradas, alterados);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha na rotina de pagamentos dos clientes.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
