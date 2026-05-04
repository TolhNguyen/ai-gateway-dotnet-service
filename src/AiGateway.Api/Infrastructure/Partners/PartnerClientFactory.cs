namespace AiGateway.Api.Infrastructure.Partners;

public sealed class PartnerClientFactory
{
    private readonly IReadOnlyDictionary<string, IAiPartnerClient> _clients;

    public PartnerClientFactory(IEnumerable<IAiPartnerClient> clients)
    {
        _clients = clients.ToDictionary(x => x.AdapterCode, StringComparer.OrdinalIgnoreCase);
    }

    public IAiPartnerClient GetClient(string adapterCode)
    {
        if (_clients.TryGetValue(adapterCode, out var client))
        {
            return client;
        }

        throw new InvalidOperationException($"AI partner adapter not registered: {adapterCode}");
    }
}
