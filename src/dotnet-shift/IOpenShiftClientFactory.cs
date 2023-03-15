interface IOpenShiftClientFactory
{
    IOpenShiftClient CreateClient(LoginContext login);
}