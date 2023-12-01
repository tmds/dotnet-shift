using System.Security.Cryptography.X509Certificates;

sealed class LoginContext
{
    public required string Name { get; set; }
    public required string Server { get; set; }
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string Namespace { get; set; }
    public required bool SkipTlsVerify { get; set; }
    public X509Certificate2Collection? CACerts { get; set; }
}