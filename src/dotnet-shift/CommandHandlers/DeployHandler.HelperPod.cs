using System.Formats.Tar;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;
using OpenShift;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace CommandHandlers;

sealed partial class DeployHandler
{
    private async Task<HelperPod> StartHelperPodAsync(IOpenShiftClient client, string runtimeVersion, CancellationToken cancellationToken)
    {
        var pod = new HelperPod(client, Console);

        // The helper pod image must have .NET runtime 6.0 or higher
        // and include tar, a shell, and some common utils like dd, touch, and stat.
        string image = $"{InternalRegistryHostName}/{client.Namespace}/{DotnetRuntimeImageStreamName}:{runtimeVersion}";

        try
        {
            await pod.RunAsync(image, cancellationToken);

            return pod;
        }
        catch
        {
            await pod.DisposeAsync();

            throw;
        }
    }

    sealed class HelperPod : IAsyncDisposable
    {
        // The pod checks the file at the interval, and we touch it at half the interval.
        // If we fail to touch it in the interval, it will terminate.
        private const int KeepAliveCheckInterval = 120; // 2 min.
        private const string KeepAliveFileName = "/tmp/keep_alive";

        private readonly List<Task> _tasks = new();
        private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);
        private readonly CancellationTokenSource _cts = new();
        private readonly IOpenShiftClient _client;
        private readonly IAnsiConsole Console;

        private string? _podName;
        private bool _helperCopied;
        private volatile bool _disposed;

        public HelperPod(IOpenShiftClient client, IAnsiConsole console)
        {
            _client = client;
            Console = console;
        }

        private async Task<RemoteProcess> StartDotNetHelperAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (!_helperCopied)
                {
                    await CopyFileAsync(Path.Combine(AppContext.BaseDirectory, "dsh.dll"), "/tmp/dsh/dsh.dll", cancellationToken);
                    await CopyFileAsync(Path.Combine(AppContext.BaseDirectory, "dsh.runtimeconfig.json"), "/tmp/dsh/dsh.runtimeconfig.json", cancellationToken);
                    _helperCopied = true;
                }
            }
            finally
            {
                _semaphore.Release();
            }

            return await ExecAsync(["dotnet", "/tmp/dsh/dsh.dll"], cancellationToken);
        }

        private Task<RemoteProcess> ExecAsync(IEnumerable<string> command, CancellationToken cancellationToken)
            => _client.PodExecAsync(_podName!, command, cancellationToken);

        private Task<PortForward> PortForwardAsync(int port, CancellationToken cancellationToken)
            => _client.PodForwardAsync(_podName!, port, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            // Set this at the start so we can filter out exceptions due to disposing.
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_podName != null)
            {
                try
                {
                    await _client.DeletePodAsync(_podName, new CancellationTokenSource(TimeSpan.FromMilliseconds(100)).Token);
                }
                catch
                { }
            }

            try
            {
                _cts.Cancel();
                await Task.WhenAll(_tasks);
                _tasks.Clear();
            }
            catch
            { }
        }

        public async Task<X509Certificate2Collection> GetServiceCaBundleAsync(CancellationToken cancellationToken)
        {
            RemoteProcess process = await _client.PodExecAsync(_podName!, ["cat", "/var/run/secrets/kubernetes.io/serviceaccount/service-ca.crt"], cancellationToken);
            MemoryStream stdout = new();
            MemoryStream stderr = new();
            await process.ReadToEndAsync(stdout, stderr, cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new ProcessFailedException(process.ExitCode, new StreamReader(stdout).ReadToEnd());
            }

            X509Certificate2Collection caCerts = new();
            stdout.Position = 0;
            caCerts.ImportFromPem(Encoding.ASCII.GetString(stdout.ToArray()));
            return caCerts;
        }

        private async Task CopyFileAsync(string fileName, string targetFile, CancellationToken cancellationToken)
        {
            Stream archive = CreateArchiveFromFile(fileName, targetFile);

            // Write to a memory stream so we can determine the length.
            MemoryStream ms = new MemoryStream();
            await archive.CopyToAsync(ms);
            long length = ms.Length;
            ms.Position = 0;

            // Use 'dd' to inject an EOF at the end of the archive.
            RemoteProcess process = await _client.PodExecAsync(_podName!, ["/bin/sh", "-c", $"dd count={length} iflag=count_bytes  | tar xmf - -C /"], cancellationToken);

            try
            {
                await ms.CopyToAsync(process.StandardInputStream);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex);
            }

            await WaitForExitSuccess(process, cancellationToken);
        }

        private static Stream CreateArchiveFromFile(string fileName, string targetFile)
        {
            // TODO: finetune PipeOptions.
            // TODO: Compress.
            return TarStreamFromDirectory(fileName, targetFile);

            static Stream TarStreamFromDirectory(string fileName, string targetFile)
            {
                var pipe = new Pipe();

                TarFileToPipeWriter(fileName, targetFile, pipe.Writer);

                return pipe.Reader.AsStream();

                static async void TarFileToPipeWriter(string fileName, string targetFile, PipeWriter writer)
                {
                    try
                    {
                        await CreateFromFileAsync(fileName, targetFile, writer.AsStream(leaveOpen: true));
                        writer.Complete();
                    }
                    catch (Exception ex)
                    {
                        writer.Complete(ex);
                    }
                }

                static async Task CreateFromFileAsync(string fileName, string targetFile, Stream destination)
                {
                    TarWriter writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: false);
                    await using (writer.ConfigureAwait(false))
                    {
                        await writer.WriteEntryAsync(fileName, targetFile, cancellationToken: default).ConfigureAwait(false);
                    }
                }
            }

            // static Stream Compress(Stream stream)
            // {
            //     var pipe = new Pipe();

            //     StreamToPipeWriter(stream, pipe.Writer);

            //     return pipe.Reader.AsStream();

            //     static async void StreamToPipeWriter(Stream stream, PipeWriter writer)
            //     {
            //         using GZipStream gzStream = new GZipStream(writer.AsStream(leaveOpen: true), CompressionMode.Compress);
            //         try
            //         {
            //             await stream.CopyToAsync(gzStream);
            //             gzStream.Close();
            //             writer.Complete();
            //         }
            //         catch (Exception ex)
            //         {
            //             try
            //             {
            //                 gzStream.Close();
            //             }
            //             catch
            //             { }
            //             writer.Complete(ex);
            //         }
            //     }
            // }
        }

        public async Task RunAsync(string image, CancellationToken cancellationToken)
        {
            _podName = $"dsh-{RandomName.Generate()}";

            List<string> command = ["/bin/sh", "-c", $"""set -euo pipefail; echo "dotnet-shift helper pod"; touch {KeepAliveFileName}; while [ "$(( $(date +%s) - $(stat {KeepAliveFileName} -c %Y) ))" -lt {KeepAliveCheckInterval} ]; do sleep {KeepAliveCheckInterval}; done"""];

            var pod = await _client.CreatePodAsync(
                new Pod()
                {
                    Metadata = new()
                    {
                        Name = _podName,
                        Labels = new Dictionary<string, string>()
                        {
                            { ResourceLabels.PartOf, ResourceLabelValues.ManagedByDotnetShift },
                            { ResourceLabels.ManagedBy, ResourceLabelValues.ManagedByDotnetShift },
                            { ResourceLabels.Runtime, ResourceLabelValues.DotnetRuntime }
                        }
                    },
                    Spec = new()
                    {
                        TerminationGracePeriodSeconds = 0,
                        Containers = new()
                            {
                                new ()
                                {
                                    Name = "main",
                                    Image = image,
                                    Command = command,
                                    Resources = new()
                                    {
                                        Requests = new Dictionary<string, string>()
                                        {
                                            { "memory", "50Mi" }
                                        },
                                        Limits = new Dictionary<string, string>()
                                        {
                                            { "memory", "50Mi" }
                                        }
                                    }
                                }
                            },
                        RestartPolicy = PodSpecRestartPolicy.Never
                    }
                }
            , default);

            PodStatusPhase? phase;
            do
            {
                await Task.Delay(100);

                pod = await _client.GetPodAsync(_podName, default);

                if (pod == null)
                {
                    throw new InvalidOperationException("Pod not found.");
                }

                phase = pod.Status.Phase;
            } while (phase == null || phase == PodStatusPhase.Pending);

            AddTask(TickleAsync(_cts.Token));
        }

        private void AddTask(Task task)
        {
            _tasks.Add(CatchExceptionTask(task));
        }

        private async Task CatchExceptionTask(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // assume exception after _disposed are due to disposing and don't print them.
                if (!_disposed)
                {
                    Console.WriteErrorLine("An error occured in the helper pod:");
                    Console.WriteException(ex);
                }
            }
        }

        private async Task TickleAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var remoteProcess = await _client.PodExecAsync(_podName!, new[] { "/bin/sh", "-c", $"touch {KeepAliveFileName}" }, ct);
                await WaitForExitSuccess(remoteProcess, ct);
                await Task.Delay(TimeSpan.FromSeconds(KeepAliveCheckInterval / 2), ct);
            }
        }

        private async Task WaitForExitSuccess(RemoteProcess process, CancellationToken ct)
        {
            StringBuilder sb = new();
            while (true)
            {
                (bool isError, string? line) = await process.ReadLineAsync(readStdout: false, readStderr: true, ct);
                if (line == null)
                {
                    break;
                }
                sb.AppendLine(line);
            }
            if (process.ExitCode != 0)
            {
                throw new ProcessFailedException(process.ExitCode, sb);
            }
        }

        public class ProcessFailedException : System.Exception
        {
            public ProcessFailedException(int exitCode, StringBuilder processOutput) :
                this(exitCode, processOutput.ToString())
            { }
            public ProcessFailedException(int exitCode, string processOutput) :
                base($"Process failed with {exitCode}.{Environment.NewLine}{processOutput}")
            { }
        }

        public async Task<Uri> RemoteProxyToInternalRegistryAsync(X509Certificate2Collection serviceCaCerts, CancellationToken cancellationToken)
        {
            // The helper pod process forwards TCP port 5000 to image-registry.openshift-image-registry.svc:5000.
            RemoteProcess dotnetHelper = await StartDotNetHelperAsync(cancellationToken);

            // Use YARP to proxy from http localhost to the helper pod.
            var builder = WebApplication.CreateBuilder();

            builder.Services.AddHttpForwarder();
            // Don't log.
            builder.Logging.ClearProviders();

            // Don't hook up signal handlers for shutdown, use the CancellationToken only.
            builder.Services.AddSingleton<IHostLifetime, NullLifetime>();

            var app = builder.Build();

            var httpClient = new HttpMessageInvoker(new SocketsHttpHandler()
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
                ConnectTimeout = TimeSpan.FromSeconds(15),
                SslOptions = new()
                {
                    RemoteCertificateValidationCallback = (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                    {
                        if (sslPolicyErrors == SslPolicyErrors.None)
                        {
                            return true;
                        }

                        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                        {
                            chain!.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            chain.ChainPolicy.ExtraStore.AddRange(serviceCaCerts);
                            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                            bool isValid = chain.Build((X509Certificate2)certificate!);
                            if (!isValid)
                            {
                                return false;
                            }

                            foreach (var caCert in serviceCaCerts)
                            {
                                bool isTrusted = chain.Build(caCert);
                                if (isTrusted)
                                {
                                    return true;
                                }
                            }
                        }

                        return false;
                    }
                },
                ConnectCallback = async (SocketsHttpConnectionContext ctx, CancellationToken ct) =>
                {
                    var stream = await PortForwardAsync(5000, ct);
                    return stream;
                }
            });

            app.UseRouting();

            var transformer = new CustomTransformer();
            var requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };

            app.MapForwarder("/{**catch-all}", "https://image-registry.openshift-image-registry.svc:5000", requestOptions, transformer, httpClient);

            app.Urls.Clear();
            app.Urls.Add("http://127.0.0.1:0");

            AddTask(app.StartAsync(_cts.Token));

            return new Uri(app.Urls.First());
        }

        class CustomTransformer : HttpTransformer
        {
            public override ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
            {
                // The OpenShift internal registry responds with the 'https' scheme, change it to 'http' for our proxy. 
                if (proxyResponse is not null)
                {
                    var authenticateHeader = proxyResponse.Headers.WwwAuthenticate;
                    foreach (var value in authenticateHeader)
                    {
                        if (value.Scheme == "Bearer")
                        {
                            authenticateHeader.Remove(value);
                            string? parameter = value.Parameter?.Replace("realm=\"https://localhost", "realm=\"http://localhost");
                            authenticateHeader.Add(new AuthenticationHeaderValue(value.Scheme, parameter));
                            break;
                        }
                    }
                    if (proxyResponse.Headers.Location is { IsAbsoluteUri: true, Scheme: "https", Host: "localhost" })
                    {
                        UriBuilder builder = new(proxyResponse.Headers.Location);
                        builder.Scheme = "http";
                        proxyResponse.Headers.Location = builder.Uri;
                    }
                }
                return base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            }
        }

        sealed class NullLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}