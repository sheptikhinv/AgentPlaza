namespace AgentPlaza.Api;

/// <summary>Periodically removes stale agent sessions from current presence.</summary>
public sealed class PresenceExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PresenceExpirationWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PresenceService>().ExpireStaleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire stale Agent Plaza sessions");
            }
        }
    }
}
