using System.Buffers;
using System.Net.WebSockets;
using Microsoft.Extensions.ObjectPool;

namespace OpenShift
{
    public class PortForward : Stream
    {
        private readonly WebSocket _webSocket;
        private readonly bool[] _receivedPortHeader = new bool[2];
        private readonly byte[] _tinyBuffer = new byte[2];
        private bool _eof;

        internal PortForward(WebSocket websocket)
        {
            _webSocket = websocket;

            CurrentReadType = WebSocketReadType.None;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _webSocket.Dispose();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        { }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count), default(CancellationToken)).GetAwaiter().GetResult();

        public override void Write(byte[] buffer, int offset, int count)
            => WriteAsync(buffer.AsMemory(offset, count), default(CancellationToken)).GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_eof)
            {
                throw new InvalidOperationException("Reading past the end of the stream.");
            }

            while (true)
            {
                ValueWebSocketReceiveResult receiveResult;
                WebSocketReadType currentReadType = CurrentReadType;

                // Read the first byte which has the WebSocketReadType.
                if (currentReadType == WebSocketReadType.None)
                {
                    receiveResult = await _webSocket.ReceiveAsync(_tinyBuffer.AsMemory(0, 1), cancellationToken);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Assert(receiveResult.Count == 0);
                        _eof = true;
                        return 0;
                    }

                    Debug.Assert(receiveResult.Count > 0);

                    currentReadType = CurrentReadType; // reads from _tinyBuffer.
                    if (receiveResult.EndOfMessage)
                    {
                        CurrentReadType = WebSocketReadType.None;
                        continue;
                    }
                }

                // The port is send as two bytes before any data.
                bool receivedPortHeader = _receivedPortHeader[(int)currentReadType];
                if (!receivedPortHeader)
                {
                    receiveResult = await _webSocket.ReceiveAsync(_tinyBuffer.AsMemory(), cancellationToken);

                    if (!receiveResult.EndOfMessage || receiveResult.Count != 2)
                    {
                        throw new InvalidOperationException($"Expected to receive port number header. Received count: {receiveResult.Count} end of message: {receiveResult.EndOfMessage}.");
                    }

                    _receivedPortHeader[(int)currentReadType] = true;
                    CurrentReadType = WebSocketReadType.None;
                    continue;
                }

                if (currentReadType != WebSocketReadType.Data)
                {
                    throw new InvalidOperationException($"Expected to receive channel data. Received {currentReadType}.");
                }

                receiveResult = await _webSocket.ReceiveAsync(buffer, cancellationToken);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Assert(receiveResult.Count == 0);
                    _eof = true;
                    return 0;
                }

                if (receiveResult.EndOfMessage)
                {
                    CurrentReadType = WebSocketReadType.None;
                }

                if (receiveResult.Count > 0)
                {
                    return receiveResult.Count;
                }
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var sendBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length + 1);
            try
            {
                sendBuffer[0] = 0;
                buffer.CopyTo(sendBuffer.AsMemory(1));
                await _webSocket.SendAsync(sendBuffer.AsMemory(0, buffer.Length + 1), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sendBuffer);
            }
        }

        private WebSocketReadType CurrentReadType
        {
            get => (WebSocketReadType)_tinyBuffer[0];
            set => _tinyBuffer[0] = (byte)value;
        }

        enum WebSocketReadType : byte
        {
            None = 255,
            Data = 0,
            Error = 1
        }
    }
}