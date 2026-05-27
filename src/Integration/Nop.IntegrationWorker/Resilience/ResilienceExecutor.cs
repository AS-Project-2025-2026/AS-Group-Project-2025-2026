using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Options;

namespace Nop.IntegrationWorker.Resilience;

public sealed class ResilienceExecutor
{
    private readonly CircuitBreakerRegistry _registry;
    private readonly ResilienceOptions _opts;
    private readonly ILogger<ResilienceExecutor> _logger;

    public ResilienceExecutor(
        CircuitBreakerRegistry registry,
        IOptions<ResilienceOptions> opts,
        ILogger<ResilienceExecutor> logger)
    {
        _registry = registry;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes <paramref name="operation"/> with exponential backoff retry and circuit breaker.
    /// Returns (success, lastError, attemptCount).
    /// </summary>
    public async Task<(bool Success, string? LastError, int Attempts)> ExecuteAsync(
        string adapter,
        string correlationId,
        string idempotencyKey,
        int? outboxRecordId,
        Func<CancellationToken, Task> operation,
        CancellationToken ct)
    {
        var circuit = _registry.Get(adapter);
        string? lastError = null;
        int attempt = 0;

        while (attempt <= _opts.MaxRetryAttempts)
        {
            ct.ThrowIfCancellationRequested();

            // Transition Open → HalfOpen when cooldown elapsed
            circuit.TransitionToHalfOpen();

            if (!circuit.IsCallAllowed())
            {
                lastError = $"Circuit breaker OPEN for adapter '{adapter}', next probe at {circuit.NextProbeAtUtc:O}";
                _logger.LogWarning(
                    "Circuit breaker OPEN — skipping call. Adapter={Adapter} CorrelationId={CorrelationId} NextProbeAt={NextProbeAt} CircuitState={CircuitState}",
                    adapter, correlationId, circuit.NextProbeAtUtc, circuit.State);
                return (false, lastError, attempt);
            }

            if (circuit.State == CircuitState.HalfOpen)
                _logger.LogInformation(
                    "Circuit breaker HALF-OPEN probe. Adapter={Adapter} CorrelationId={CorrelationId} CircuitState={CircuitState}",
                    adapter, correlationId, circuit.State);

            try
            {
                _logger.LogInformation(
                    "Adapter call started. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OutboxRecordId={OutboxRecordId} RetryAttempt={RetryAttempt}",
                    adapter, correlationId, idempotencyKey, outboxRecordId, attempt);

                await operation(ct);

                circuit.RecordSuccess();

                if (attempt > 0)
                    _logger.LogInformation(
                        "Circuit breaker CLOSED/recovered. Adapter={Adapter} CorrelationId={CorrelationId} CircuitState={CircuitState}",
                        adapter, correlationId, circuit.State);

                _logger.LogInformation(
                    "Adapter call succeeded. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OutboxRecordId={OutboxRecordId} RetryAttempt={RetryAttempt}",
                    adapter, correlationId, idempotencyKey, outboxRecordId, attempt);

                return (true, null, attempt + 1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                circuit.RecordFailure(ex.Message);

                _logger.LogWarning(
                    "Adapter call failed. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OutboxRecordId={OutboxRecordId} RetryAttempt={RetryAttempt} Error={Error} CircuitState={CircuitState}",
                    adapter, correlationId, idempotencyKey, outboxRecordId, attempt, ex.Message, circuit.State);

                if (circuit.State == CircuitState.Open)
                    _logger.LogWarning(
                        "Circuit breaker OPENED. Adapter={Adapter} CorrelationId={CorrelationId} ConsecutiveFailures={ConsecutiveFailures} NextProbeAt={NextProbeAt} CircuitState={CircuitState}",
                        adapter, correlationId, circuit.ConsecutiveFailures, circuit.NextProbeAtUtc, circuit.State);

                attempt++;

                if (attempt > _opts.MaxRetryAttempts)
                    break;

                var delay = CalculateDelay(attempt);
                _logger.LogInformation(
                    "Retry scheduled. Adapter={Adapter} CorrelationId={CorrelationId} RetryAttempt={RetryAttempt} DelaySeconds={DelaySeconds}",
                    adapter, correlationId, attempt, delay.TotalSeconds);

                await Task.Delay(delay, ct);
            }
        }

        return (false, lastError, attempt);
    }

    public async Task PersistCircuitStateAsync(Data.WorkerDataService data, string adapter)
    {
        try
        {
            var circuit = _registry.Get(adapter);
            await data.UpsertCircuitBreakerStateAsync(
                adapter: adapter,
                state: circuit.State.ToString(),
                failureCount: circuit.ConsecutiveFailures,
                openedAtUtc: circuit.OpenedAtUtc,
                nextProbeAtUtc: circuit.NextProbeAtUtc,
                lastError: circuit.LastError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist circuit breaker state for adapter '{Adapter}'", adapter);
        }
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var seconds = _opts.InitialDelaySeconds * Math.Pow(2, attempt - 1);
        seconds = Math.Min(seconds, _opts.MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
