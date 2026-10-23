namespace Algara.Web.Services.Email;

public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Email:WorkerEnabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                worked = await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Exception/SMTP text may contain message data or tokens. Leases recover after restart.
                logger.LogWarning("Email delivery is temporarily unavailable; a later worker iteration will retry.");
            }
            try { await Task.Delay(worked ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
