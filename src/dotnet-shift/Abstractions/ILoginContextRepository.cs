interface ILoginContextRepository
{
    void UpdateContext(LoginContext context, bool setCurrent);
    LoginContext? GetCurrentContext();
    List<LoginContext> GetAllContexts();
    void SetCurrentContext(string contextName);
    bool DeleteContext(string contextName);
    LoginContext? GetContext(string contextName);
    string GetDefaultName(LoginContext login);
}