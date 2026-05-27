using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Options;

namespace Nop.IntegrationWorker.Resilience;

public sealed class CircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, AdapterCircuit> _circuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ResilienceOptions _opts;

    public CircuitBreakerRegistry(IOptions<ResilienceOptions> opts)
    {
        _opts = opts.Value;
    }

    public AdapterCircuit Get(string adapter) =>
        _circuits.GetOrAdd(adapter, k => new AdapterCircuit(k, _opts.CircuitFailureThreshold, _opts.CircuitCooldownSeconds));

    public IReadOnlyCollection<AdapterCircuit> All() => _circuits.Values.ToList();
}
