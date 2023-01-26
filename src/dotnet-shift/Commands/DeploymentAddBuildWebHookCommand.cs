using System.CommandLine;
using System.Security.Cryptography;
using System.Text;

sealed class DeploymentAddBuildWebHookCommand : Command
{
    private const string Github = "github";
    private const string Gitlab = "gitlab";
    private const string Bitbucket = "bitbucket";

    public DeploymentAddBuildWebHookCommand() : base("add-build-webhook", "Add a webhook")
    {
        Add(DeploymentArgument);
        Add(TypeOption);

        this.SetHandler(HandleAsync, DeploymentArgument, TypeOption);
    }

    public static async Task<int> HandleAsync(string deployment, string type)
    {
        var loginContext = KubernetesClientConfigFile.GetDefaultContext();

        var client = new OpenShiftClient(loginContext);

        string secretName = $"{deployment}-{type}";
        string webhookSecretValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        string appName = deployment; // TODO ...
        string secret = GenerateSecret(secretName, appName, webhookSecretValue);
        bool created = await client.TryCreateSecretAsync(secret);
        if (!created)
        {
            Console.WriteLine($"Secret '{secretName}' already exists.");
            return 1;
        }

        string buildConfigPatch = GenerateBuildConfigPatch(type, secretName);
        string buildConfigName = deployment; // TODO ...
        await client.PatchBuildConfigAsync(buildConfigName, buildConfigPatch);

        string url = $"{loginContext.Server}/apis/build.openshift.io/v1/namespaces/{loginContext.Namespace}/buildconfigs/{buildConfigName}/webhooks/{webhookSecretValue}/{type}";
        Console.WriteLine($"You can use the following webhook: {url}");

        return 0;
    }

    public static readonly Argument<string> DeploymentArgument =
        new Argument<string>("DEPLOYMENT", "Name of the deployment");

    public static readonly Option<string> TypeOption =
        new Option<string>("--type", "Type of webhook")
        {
            IsRequired = true
        };

    static DeploymentAddBuildWebHookCommand()
    {
        TypeOption.AcceptOnlyFromAmong(Bitbucket, Github, Gitlab);
    }

    private static string GenerateBuildConfigPatch(string triggerType, string secretName)
    {
        // TODO: this replaces all triggers, though we want to add one.

        string configTriggerType = triggerType switch
        {
            Github => "GitHub",
            Gitlab => "GitLab",
            Bitbucket => "Bitbucket",
            _ => throw new NotSupportedException(triggerType),
        };

        return $$"""
        {
            "spec": {
                "triggers": [
                    {
                        "type": "{{configTriggerType}}",
                        "{{configTriggerType.ToLowerInvariant()}}": {
                            "secretReference": {
                                "name": "{{secretName}}"
                            }
                        }
                    }
                ]
            }
        }
        """;
    }

    private static string GenerateSecret(string name, string appName, string key)
    {
        return $$"""
        {
            "kind": "Secret",
            "apiVersion": "v1",
            "metadata": {
                "name": "{{name}}",
                "labels": {
                    "app.kubernetes.io/managed-by": "{{LabelConstants.DotnetShift}}",
                    "app.kubernetes.io/part-of": "{{appName}}"
                }
            },
            "data": {
                "WebHookSecretKey": "{{Convert.ToBase64String(Encoding.UTF8.GetBytes(key))}}"
            }
        }
        """;
    }
}