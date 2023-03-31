using System.CommandLine;

namespace Cli;

sealed partial class AppCommandLine
{
    public static class Options
    {
        private const string APP = nameof(APP);
        private const string DEPLOYMENT = nameof(DEPLOYMENT);
        private const string CONTEXT = nameof(CONTEXT);
        private const string PROJECT = nameof(PROJECT);

        public static readonly CliOption<string> RequiredServerOption =
            new CliOption<string>("--server")
            {
                Description = "The address and port of the Kubernetes API server",
                Required = true
            };

        public static readonly CliOption<string> RequiredTokenOption =
            new CliOption<string>("--token")
            {
                Description = "Bearer token for authentication to the API server",
                Required = true
            };

        public static readonly CliOption<bool> InsecureSkipTlsVerifyOption =
            new CliOption<bool>("--insecure-skip-tls-verify")
            {
                Description = "If true, the server's certificate will not be checked for validity (insecure)",
                Arity = ArgumentArity.Zero
            };

        public static readonly CliOption<string?> NamespaceOption =
            new CliOption<string?>("--namespace")
            {
                Description = "The Kubernetes namespace"
            };

        public static readonly CliOption<string> LoginNameOption =
            new CliOption<string>("--name")
            {
                Description = "Name the connection",
                HelpName = CONTEXT
            };

        public static readonly CliArgument<string> RequiredContextArgument =
            new CliArgument<string>(CONTEXT)
            { };

        public static readonly CliArgument<string> RequiredDeployProjectArgument =
            new CliArgument<string>(PROJECT)
            {
                DefaultValueFactory = _ => "."
            };

        public static readonly CliOption<bool> ExposeOption =
            new CliOption<bool>("--expose")
            {
                Description = "Make the application accessible externally",
                Arity = ArgumentArity.Zero
            };

        public static readonly CliOption<bool> NoFollowOption =
            new CliOption<bool>("--no-follow")
            {
                Description = "Do not follow progress",
                Arity = ArgumentArity.Zero
            };

        public static readonly CliOption<bool> NoBuildOption =
            new CliOption<bool>("--no-build")
            {
                Description = "Do not start a build",
                Arity = ArgumentArity.Zero
            };

        public static readonly CliOption<string> PartOfOption =
            new CliOption<string>("--part-of")
            {
                Description = "Add to application",
                HelpName = APP
            };

        public static readonly CliOption<string> DeploymentNameOption =
            new CliOption<string>("--name")
            {
                Description = "Name the deployment",
                HelpName = DEPLOYMENT
            };

        public static readonly CliArgument<string> RequiredAppArgument =
            new CliArgument<string>(APP)
            { };

        public static readonly CliOption<string?> ContextOption =
            new CliOption<string?>("--context")
            {
                Description = "The connection context [default: current context]"
            };
    }
}