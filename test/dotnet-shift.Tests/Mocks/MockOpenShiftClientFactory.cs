sealed class MockOpenShiftClientFactory : IOpenShiftClientFactory
{
    private readonly MockOpenShiftServer _server;

    public MockOpenShiftClientFactory(MockOpenShiftServer server)
        => _server = server;

    public IOpenShiftClient CreateClient(LoginContext login)
        => new MockOpenShiftClient(_server);
}