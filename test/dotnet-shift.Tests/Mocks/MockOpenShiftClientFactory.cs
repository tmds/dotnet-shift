sealed class MockOpenShiftClientFactory : IOpenShiftClientFactory
{
    private readonly IOpenShiftClient _client;

    public MockOpenShiftClientFactory(IOpenShiftClient client)
        => _client = client;

    public IOpenShiftClient CreateClient(LoginContext login)
        => _client;
}