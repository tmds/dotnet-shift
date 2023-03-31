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

        public static readonly Option<string> RequiredServerOption =
            new Option<string>("--server")
            {
                Description = "The address and port of the Kubernetes API server",
                IsRequired = true
            };

        public static readonly Option<string> RequiredTokenOption =
            new Option<string>("--token")
            {
                Description = "Bearer token for authentication to the API server",
                IsRequired = true
            };

        public static readonly Option<bool> InsecureSkipTlsVerifyOption =
            new Option<bool>("--insecure-skip-tls-verify")
            {
                Description = "If true, the server's certificate will not be checked for validity (insecure)",
                Arity = ArgumentArity.Zero
            };

        public static readonly Option<string?> NamespaceOption =
            new Option<string?>("--namespace")
            {
                Description = "The Kubernetes namespace"
            };

        public static readonly Option<string> LoginNameOption =
            new Option<string>("--name")
            {
                Description = "Name the connection",
                HelpName = CONTEXT
            };

        public static readonly Argument<string> RequiredContextArgument =
            new Argument<string>(CONTEXT)
            { };

        public static readonly Argument<string> RequiredDeployProjectArgument =
            new Argument<string>(PROJECT)
            {
                DefaultValueFactory = _ => "."
            };

        public static readonly Option<bool> ExposeOption =
            new Option<bool>("--expose")
            {
                Description = "Make the application accessible externally",
                Arity = ArgumentArity.Zero
            };

        public static readonly Option<bool> NoFollowOption =
            new Option<bool>("--no-follow")
            {
                Description = "Do not follow progress",
                Arity = ArgumentArity.Zero
            };

        public static readonly Option<bool> NoBuildOption =
            new Option<bool>("--no-build")
            {
                Description = "Do not start a build",
                Arity = ArgumentArity.Zero
            };

        public static readonly Option<string> PartOfOption =
            new Option<string>("--part-of")
            {
                Description = "Add to application",
                HelpName = APP
            };

        public static readonly Option<string> DeploymentNameOption =
            new Option<string>("--name")
            {
                Description = "Name the deployment",
                HelpName = DEPLOYMENT
            };

        public static readonly Argument<string> RequiredAppArgument =
            new Argument<string>(APP)
            { };

        public static readonly Option<string?> ContextOption =
            new Option<string?>("--context")
            {
                Description = "The connection context [default: current context]"
            };
    }
}