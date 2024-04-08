// The code in this file was imported from https://github.com/tmds/Tmds.Ssh.
// The code is under the MIT License.

using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OpenShift
{
    class WebSocketProcess : Process
    {
        private readonly WebSocket _webSocket;
        private readonly byte[] _currentReadType = new byte[1];

        enum WebSocketReadType
        {
            None = 0,
            StandardOutput = 1,
            StandardError = 2,
            Exited = 3,
        }

        public WebSocketProcess(WebSocket webSocket) { _webSocket = webSocket; }

        public override void Dispose()
        {
            base.Dispose();
            _webSocket.Dispose();
        }

        private protected override async ValueTask<ProcessReadResult> ReadProcessAsync(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken = default)
        {
            byte[]? rentedBuffer = null;
            try
            {
                while (true)
                {
                    ValueWebSocketReceiveResult receiveResult;
                    WebSocketReadType currentReadType = (WebSocketReadType)_currentReadType[0];

                    if (currentReadType == WebSocketReadType.None)
                    {
                        receiveResult = await _webSocket.ReceiveAsync(_currentReadType.AsMemory(0, 1), cancellationToken);

                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.Assert(receiveResult.Count == 0);
                            return new ProcessReadResult(ProcessReadType.ProcessExit);
                        }

                        Debug.Assert(receiveResult.Count > 0);

                        currentReadType = (WebSocketReadType)_currentReadType[0];
                        if (receiveResult.EndOfMessage)
                        {
                            _currentReadType[0] = (byte)WebSocketReadType.None;
                            continue;
                        }
                    }

                    Memory<byte> receiveBuffer = currentReadType switch
                    {
                        WebSocketReadType.StandardOutput => stdoutBuffer,
                        WebSocketReadType.StandardError => stderrBuffer,
                        _ => null
                    } ?? (rentedBuffer ??= ArrayPool<byte>.Shared.Rent(4096));

                    receiveResult = await _webSocket.ReceiveAsync(receiveBuffer, cancellationToken);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        return new ProcessReadResult(ProcessReadType.ProcessExit);
                    }

                    if (receiveResult.EndOfMessage)
                    {
                        _currentReadType[0] = (byte)WebSocketReadType.None;
                    }

                    if (receiveResult.Count > 0)
                    {
                        if (currentReadType == WebSocketReadType.StandardOutput && !stdoutBuffer.HasValue)
                        {
                            return new ProcessReadResult(ProcessReadType.StandardOutput, receiveResult.Count);
                        }
                        else if (currentReadType == WebSocketReadType.StandardError && !stderrBuffer.HasValue)
                        {
                            return new ProcessReadResult(ProcessReadType.StandardError, receiveResult.Count);
                        }
                        else if (currentReadType == WebSocketReadType.Exited)
                        {
                            if (!receiveResult.EndOfMessage)
                            {
                                throw new NotSupportedException();
                            }
                            JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(receiveBuffer.Span.Slice(0, receiveResult.Count)));
                            string status = doc.RootElement.GetProperty("status").GetString()!;
                            if (status == "Success")
                            {
                                // {"metadata":{},"status":"Success"}
                                SetExitCode(0);
                            }
                            else if (status == "Failure")
                            {
                                // {"metadata":{},"status":"Failure","message":"command terminated with non-zero exit code: exit status 255","reason":"NonZeroExitCode","details":{"causes":[{"reason":"ExitCode","message":"255"}]}}
                                if (doc.RootElement.TryGetProperty("details", out JsonElement details) &&
                                    details.TryGetProperty("causes", out JsonElement causes) &&
                                    causes.EnumerateArray() is var causeItems &&
                                    causeItems.FirstOrDefault(item => item.TryGetProperty("reason", out JsonElement reason) && reason.GetString() == "ExitCode") is var reason)
                                {
                                    int exitCode = int.Parse(reason.GetProperty("message").GetString()!);
                                    SetExitCode(exitCode);
                                }
                                else
                                {
                                    SetExitCode(-1);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        private protected override async ValueTask WriteStandardInputAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var sendBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length + 1);

            try
            {
                sendBuffer[0] = 0; // stdin
                buffer.CopyTo(sendBuffer.AsMemory(1));
                await _webSocket.SendAsync(sendBuffer.AsMemory(0, buffer.Length + 1), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sendBuffer);
            }
        }
    }
}