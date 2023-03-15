namespace OpenShift;

sealed class OpenShiftClientFactory : IOpenShiftClientFactory
{
    public IOpenShiftClient CreateClient(LoginContext login)
        => new OpenShiftClient(login.Server, login.Token, login.Namespace, login.SkipTlsVerify);
}