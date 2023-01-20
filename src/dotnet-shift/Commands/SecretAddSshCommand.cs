using System.CommandLine;
using System.Text;
using System.Security.Cryptography ;
using System.Text.Json;

sealed class SecretAddSshCommand : Command
{
    public SecretAddSshCommand() : base("add-ssh", "Add an SSH Key secret")
    {
        Add(NameArgument);
        Add(GenerateOption);
        // Add(MatchUriOption); // TODO: figure out how this works.
        Add(KnownHostFileOption);
        Add(TypeOption);
        Add(BitsOption);

        // TODO: add '--part-of' to delete the secret with the app.

        this.SetHandler((name, generate, knownHostFile, type, keySize) => HandleAsync(name, generate, knownHostFile, type, keySize), NameArgument, GenerateOption, KnownHostFileOption, TypeOption, BitsOption);
    }

    public static async Task<int> HandleAsync(string name, bool generate, string knownHostFile, string type, int keySize)
    {
        string? matchUri = null;

        LoginContext login = KubernetesClientConfigFile.GetDefaultContext();

        var client = new OpenShiftClient(login);

        string privateKey;
        string publicKey;
        if (generate)
        {
            var keygen = new SshKeyGenerator.SshKeyGenerator(keySize);
            privateKey = keygen.ToPrivateKey();
            string comment = $"{name}@{new Uri(login.Server).Host}/{login.Namespace}";
            publicKey = keygen.ToRfcPublicKey();
        }
        else
        {
            Console.Error.WriteLine("Providing an existing key is not (yet) supported. You must specify the '--generate' option.");
            return 1;
        }

        if (!File.Exists(knownHostFile))
        {
            Console.Error.WriteLine("The known hosts file is not found.");
            return 1;
        }
        string knownHosts = File.ReadAllText(knownHostFile);

        string secret = GenerateSecret(name, matchUri, privateKey, knownHosts);

        await client.CreateSecretAsync(secret);

        Console.WriteLine("This is the public key corresponding to the secret:");
        Console.WriteLine(publicKey);

        return 0;
    }

    private static string GenerateSecret(string name, string? matchUri, string privateKey, string knownHosts)
    {
        return $$"""
        {
            "kind": "Secret",
            "apiVersion": "v1",
            "metadata": {
                "name": "{{name}}",
                "labels": {
                    "app.kubernetes.io/managed-by": "{{LabelConstants.DotnetShift}}"
                },
                "annotations": {
                    {{SourceSecretAnnotation("1", matchUri)}}
                }
            },
            "stringData": {
                    "ssh-privatekey": {{JsonSerializer.Serialize(privateKey)}},
                    "known_hosts": {{JsonSerializer.Serialize(knownHosts)}}
                }
        }
        """;

        static string SourceSecretAnnotation(string id, string? matchUri)
        {
            return matchUri is null ? "" : $$""" "build.openshift.io/source-secret-match-uri-{{id}}": {{JsonSerializer.Serialize(matchUri)}} """;
        }
    }

    public static readonly Argument<string> NameArgument =
        new Argument<string>("NAME", "Name of the secret");

    public static readonly Option<bool> GenerateOption =
        new Option<bool>(new[] { "--generate" }, "Generate a key pair") { Arity = ArgumentArity.Zero };

    public static readonly Option<string> MatchUriOption =
        new Option<string>(new[] { "--match-uri", }, "Wildcard uri used by BuildConfigs to select the secret for cloning. For example: '*://github.com/myorg/*'");

    public static readonly Option<string> KnownHostFileOption =
        new Option<string>(new[] { "--known-host-file", }, FindDefaultKnownHosts, "A 'known_host' file that contains the server keys");

    public static readonly Option<string> TypeOption =
        new Option<string>(new[] { "--type", "-t" }, defaultValueFactory: () => "rsa", "Type of key to generate");

    public static readonly Option<int> BitsOption =
        new Option<int>(new[] { "--bits", "-b" }, defaultValueFactory: () => 3072, "Number of bits in key to generate");

    private static string FindDefaultKnownHosts()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(home, ".ssh", "known_hosts");
    }

    static SecretAddSshCommand()
    {
        TypeOption.AcceptOnlyFromAmong("rsa");
    }
}
