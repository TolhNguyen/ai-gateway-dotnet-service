namespace AiGateway.Api.Infrastructure.Partners;

public sealed class PartnerClientFactory
{
    private readonly IReadOnlyDictionary<string, IAiPartnerClient> _clients;

    public PartnerClientFactory(IEnumerable<IAiPartnerClient> clients)
    {
        _clients = clients.ToDictionary(c => c.AdapterCode, StringComparer.OrdinalIgnoreCase);
    }

    public IAiPartnerClient Get(string adapterCode)
    {
        if (_clients.TryGetValue(adapterCode, out var c)) return c;
        throw new InvalidOperationException($"No partner client registered for adapter '{adapterCode}'.");
    }

    public bool Has(string adapterCode) => _clients.ContainsKey(adapterCode);
}
