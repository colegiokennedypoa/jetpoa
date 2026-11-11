using System.Net.Sockets;

namespace JetPoa.Api.Impressao;

/// <summary>Envio RAW (porta 9100) direto para a impressora de rede, sem fila do Windows.</summary>
public static class ImpressoraRede
{
    public static async Task EnviarAsync(string host, int porta, byte[] dados, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        using var tcp = new TcpClient { NoDelay = true, SendTimeout = 8000 };
        try
        {
            await tcp.ConnectAsync(host, porta, cts.Token);
            await using var s = tcp.GetStream();
            await s.WriteAsync(dados, cts.Token);
            await s.FlushAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"Impressora {host}:{porta} não respondeu (timeout).");
        }
        catch (SocketException e)
        {
            throw new IOException($"Impressora {host}:{porta} inacessível: {e.Message}");
        }
    }
}
