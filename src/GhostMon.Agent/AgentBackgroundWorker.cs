namespace GhostMon.Agent;

internal static class AgentBackgroundWorker
{
    public static async Task DelayAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(interval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
