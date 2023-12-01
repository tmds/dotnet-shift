interface ILoginContextProvider
{
    LoginContext? GetContext(string? name);
}