namespace JetPoa.Api.Fiscal;

/// <summary>A cada minuto: transmite notas em contingência e resolve notas sem resposta da SEFAZ.</summary>
public sealed class NfceWorker(NfceService nfce, ILogger<NfceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await nfce.ProcessarPendenciasAsync(stop); }
            catch (Exception e) when (!stop.IsCancellationRequested) { log.LogError(e, "Falha na rotina de NFC-e"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
