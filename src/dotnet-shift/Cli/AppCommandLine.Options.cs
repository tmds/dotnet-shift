using System.CommandLine;
using System.CommandLine.Completions;
using System.Runtime.InteropServices;

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
            {
                CompletionSources = { GetContextSuggestions },
                HelpName = CONTEXT
            };

        public static readonly CliArgument<string> RequiredDeployProjectArgument =
            new CliArgument<string>(PROJECT)
            {
                DefaultValueFactory = _ => ".",
                CompletionSources = { GetProjectSuggestions },
                HelpName = PROJECT
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
                Description = "The connection context [default: current context]",
                CompletionSources = { GetContextSuggestions },
                HelpName = CONTEXT
            };

        private static IEnumerable<CompletionItem> GetContextSuggestions(CompletionContext context)
        {
            var kubernetesConfigFile = new Kubectl.KubernetesConfigFile();

            var contexts = kubernetesConfigFile.GetAllContexts(includeTokens: false);

            return contexts.Where(c => c.Name.StartsWith(context.WordToComplete))
                           .OrderBy(c => c.Name)
                           .Select(c => new CompletionItem(c.Name));
        }

        private static IEnumerable<CompletionItem> GetProjectSuggestions(CompletionContext context)
        {
            string wordToComplete = context.WordToComplete;

            // Path that corresponds to wordToComplete.
            string pathToComplete = Path.Combine(Directory.GetCurrentDirectory(), wordToComplete);
            // Handle '~' and `~/` because the bash completion script doesn't (https://github.com/dotnet/command-line-api/issues/2142).
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                (wordToComplete == "~" || wordToComplete.StartsWith("~/")))
            {
                string home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                                                                         Environment.SpecialFolderOption.DoNotVerify));
                pathToComplete = $"{home}{wordToComplete.Substring(1)}";
            }

            // Directory to search.
            string directory;
            Func<string, bool> pathFilter;
            if (Directory.Exists(pathToComplete))
            {
                directory = pathToComplete;
                pathFilter = path => true;
            }
            else
            {
                directory = Path.GetDirectoryName(pathToComplete)!;
                if (!Directory.Exists(directory))
                {
                    return Array.Empty<CompletionItem>();
                }
                pathFilter = path => path.StartsWith(pathToComplete);
            }

            EnumerationOptions NonRecursive = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden };
            EnumerationOptions Recursive = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden, RecurseSubdirectories = true };
            const string ProjFilter = "*.??proj";

            IEnumerable<string> projectFilesInDirectory;
            IEnumerable<string> subDirectories;
            IEnumerable<string>? suggestions = null;
            do
            {
                // Directory could be anywhere, we don't want to recursively search for all project files here.
                projectFilesInDirectory = Directory.GetFiles(directory, ProjFilter, NonRecursive)
                                                   .Where(pathFilter);

                // If there are project files, consider it safe to do a recursive search for all project files.
                if (projectFilesInDirectory.Any())
                {
                    suggestions = Directory.GetFiles(directory, ProjFilter, Recursive)
                                           .Where(pathFilter)
                                           .Select(p =>
                                            {
                                                int subdirNameEnd = p.IndexOf(Path.DirectorySeparatorChar, directory.Length + 1);
                                                if (subdirNameEnd == -1)
                                                {
                                                    // project file is not in a subdirectory.
                                                    return p;
                                                }
                                                else
                                                {
                                                    // project file is in a subdirectory. Suggest the subdirectory name.
                                                    return $"{p.Substring(0, subdirNameEnd)}{Path.DirectorySeparatorChar}";
                                                }
                                            })
                                           .Distinct(); // Trim subdirectories that had multiple project files.
                    break;
                }

                // There are no project files, find the subdirectories.
                subDirectories = Directory.GetDirectories(directory, "*", NonRecursive)
                                          .Where(pathFilter);

                // If there is only a single subdirectory, look inside the directory.
                if (!projectFilesInDirectory.Any() && subDirectories.Count() == 1)
                {
                    pathFilter = path => true;
                    directory = Path.Combine(directory, subDirectories.First());
                    continue;
                }

                // There is no project file, and multiple subdirectories. Let the user pick a subdirectory.
                suggestions = subDirectories.Select(path => $"{path}{Path.DirectorySeparatorChar}");
                break;
            } while (suggestions is null);

            // Trim pathToComplete from the suggestions and replace it by wordToComplete.
            int pathToCompleteOffset = pathToComplete.Length;
            // If word to complete was empty, we need to also remove a leading directory separator.
            if (wordToComplete.Length == 0)
            {
                pathToCompleteOffset++;
            }
            suggestions = suggestions.Select(p => $"{wordToComplete}{p.Substring(pathToCompleteOffset)}");

            return suggestions.OrderBy(p => p)
                              .Select(p => new CompletionItem(p));
        }
    }
}