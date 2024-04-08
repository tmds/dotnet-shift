sealed class MockOpenShiftClientFactory : IOpenShiftClientFactory
{
    private readonly MockOpenShiftServer _server;
    private readonly string _namespace;

    public MockOpenShiftClientFactory(MockOpenShiftServer server, string @namespace)
    {
        _server = server;
        _namespace = @namespace;
    }

    public IOpenShiftClient CreateClient(LoginContext login)
        => new MockOpenShiftClient(_server, _namespace);
}