// The code in this file is based on RemoteProcess from https://github.com/tmds/Tmds.Ssh.
// The code is under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

public abstract class Process : IDisposable
{
    private protected enum ProcessReadType
    {
        StandardOutput = 1,
        StandardError = 2,
        ProcessExit = 3,
    }

    private protected struct ProcessReadResult
    {
        public ProcessReadResult(ProcessReadType type, int bytesRead = 0)
        {
            Type = type;
            BytesRead = bytesRead;
        }

        public ProcessReadType Type { get; }
        public int BytesRead { get; }
    }

    internal static readonly UTF8Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const int BufferSize = 1024;

    private readonly Encoding _standardInputEncoding = DefaultEncoding;
    private readonly Encoding _standardErrorEncoding = DefaultEncoding;
    private readonly Encoding _standardOutputEncoding = DefaultEncoding;
    private StreamWriter? _stdInWriter;
    private byte[]? _byteBuffer;
    private int? _exitCode;
    private bool _skippingStdout;
    private bool _skippingStderr;

    private protected abstract ValueTask WriteStandardInputAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    private protected abstract ValueTask<ProcessReadResult> ReadProcessAsync(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken = default);
    protected void SetExitCode(int exitCode) => _exitCode = exitCode;

    struct CharBuffer
    {
        public void Initialize(Encoding encoding)
        {
            if (_charBuffer == null)
            {
                // TODO: alloc from ArrayPool?
                _charBuffer = new char[encoding.GetMaxCharCount(BufferSize)];
                _decoder = encoding.GetDecoder();
                _sbHasNoNewlines = true;
            }
        }

        public void AppendFromEncoded(Span<byte> buffer)
        {
            if (buffer.Length == 0)
            {
                return;
            }
            int charLength = _charLen - _charPos;
            if (charLength > 0)
            {
                AppendCharsToStringBuilder();
                _sbHasNoNewlines = false;
            }
            _charPos = 0;
            _charLen = _decoder.GetChars(buffer, _charBuffer, flush: false);
            if (_charLen > _charPos && _skipNewlineChar)
            {
                if (_charBuffer[_charPos] == '\n')
                {
                    _charPos++;
                }
                _skipNewlineChar = false;
            }
        }

        private void AppendCharsToStringBuilder()
        {
            int charLength = _charLen - _charPos;
            if (_sb == null)
            {
                _sb = new StringBuilder(charLength + 80);
            }
            _sb.Append(_charBuffer.AsSpan(_charPos, charLength));
            _charPos = _charLen = 0;
        }

        public bool TryReadLine(out string? line, bool final)
        {
            line = null;
            if (_charBuffer == null)
            {
                return false;
            }
            // Check stringbuilder.
            if (_sb is { Length: > 0 } && !_sbHasNoNewlines)
            {
                for (int i = 0; i < _sb.Length; i++)
                {
                    char c = _sb[i];
                    if (c == '\r' || c == '\n')
                    {
                        _skipNewlineChar = c == '\r';
                        line = _sb.ToString(0, i);
                        if (_skipNewlineChar && (i + i) < _sb.Length)
                        {
                            if (_sb[i + 1] == '\n')
                            {
                                i++;
                            }
                            _skipNewlineChar = false;
                        }
                        _sb.Remove(0, i + 1);
                        return true;
                    }
                }
                _sbHasNoNewlines = true;
            }
            // Check chars.
            if (_charPos != _charLen)
            {
                int idx = _charBuffer.AsSpan(_charPos, _charLen - _charPos).IndexOfAny('\r', '\n');
                if (idx != -1)
                {
                    _skipNewlineChar = _charBuffer[_charPos + idx] == '\r';
                    if (_sb is { Length: > 0 })
                    {
                        _sb.Append(_charBuffer.AsSpan(_charPos, idx));
                        line = _sb.ToString();
                        _sb.Clear();
                    }
                    else
                    {
                        line = new string(_charBuffer.AsSpan(_charPos, idx));
                    }
                    _charPos += idx + 1;
                    if (_skipNewlineChar && _charPos < _charLen)
                    {
                        if (_charBuffer[_charPos] == '\n')
                        {
                            _charPos++;
                        }
                        _skipNewlineChar = false;
                    }
                    return true;
                }
            }
            if (final)
            {
                if (_charPos != _charLen || _sb is { Length: > 0 })
                {
                    line = BuildString();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                AppendCharsToStringBuilder();
                return false;
            }
        }

        public string? BuildString()
        {
            string? s;
            if (_sb is { Length: > 0 })
            {
                AppendCharsToStringBuilder();
                s = _sb.ToString();
                _sb.Clear();
            }
            else if (_charBuffer == null)
            {
                s = null;
            }
            else
            {
                s = new string(_charBuffer.AsSpan(_charPos, _charLen - _charPos));
                _charLen = _charPos = 0;
            }
            return s;
        }

        private char[] _charBuffer; // Large enough to decode _byteBuffer.
        private Decoder _decoder;
        private int _charPos;
        private int _charLen;
        private StringBuilder? _sb;
        private bool _sbHasNoNewlines;
        private bool _skipNewlineChar;
    }

    private CharBuffer _stdoutBuffer;
    private CharBuffer _stderrBuffer;

    public int ExitCode
    {
        get
        {
            if (_readMode == ReadMode.Disposed)
            {
                ThrowObjectDisposedException();
            }
            else if (_readMode != ReadMode.Exited)
            {
                throw new InvalidOperationException("The process has not yet exited.");
            }

            return _exitCode!.Value;
        }
    }

    private enum ReadMode
    {
        Initial,
        ReadBytes,
        ReadChars,
        ReadException,
        Exited,
        Disposed
    }

    private ReadMode _readMode;
    private bool _delayedExit;

    private bool HasExited { get => _readMode == ReadMode.Exited; } // delays exit until it was read by the user.

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await WriteStandardInputAsync(buffer, cancellationToken);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        var writer = StandardInputWriter;
        var autoFlush = writer.AutoFlush;
        if (!autoFlush)
        {
            writer.AutoFlush = true;
        }
        try
        {
            await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException e) // Unwrap IOException. TODO: avoid wrap and unwrap...
        {
            Debug.Assert(e.InnerException != null);
            throw e.InnerException;
        }
        finally
        {
            if (!autoFlush)
            {
                writer.AutoFlush = false;
            }
        }
    }

    public async ValueTask WriteLineAsync(ReadOnlyMemory<char> buffer = default, CancellationToken cancellationToken = default)
    {
        var writer = StandardInputWriter;
        var autoFlush = writer.AutoFlush;
        if (!autoFlush)
        {
            writer.AutoFlush = true;
        }
        try
        {
            await writer.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false); ;
        }
        catch (IOException e) // Unwrap IOException.
        {
            Debug.Assert(e.InnerException != null);
            throw e.InnerException;
        }
        finally
        {
            if (!autoFlush)
            {
                writer.AutoFlush = false;
            }
        }
    }

    public ValueTask WriteAsync(string value, CancellationToken cancellationToken = default)
        => WriteAsync(value.AsMemory(), cancellationToken);

    public ValueTask WriteLineAsync(string? value, CancellationToken cancellationToken = default)
        => WriteLineAsync(value != null ? value.AsMemory() : default, cancellationToken);

    public Stream StandardInputStream
        => StandardInputWriter.BaseStream;

    public StreamWriter StandardInputWriter
        => (_stdInWriter ??= new StreamWriter(new StdInStream(this), _standardInputEncoding) { AutoFlush = true, NewLine = "\n" });

    private async ValueTask<(ProcessReadType ReadType, int bytesRead)> ReadCore(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken)
    {
        if (stdoutBuffer is { Length: 0 })
        {
            throw new ArgumentException("Buffer length cannot be zero.", nameof(stdoutBuffer));
        }
        if (stderrBuffer is { Length: 0 })
        {
            throw new ArgumentException("Buffer length cannot be zero.", nameof(stderrBuffer));
        }

        if (stdoutBuffer.HasValue && _skippingStdout)
        {
            throw new InvalidOperationException("Standard output is being skipped.");
        }
        if (stderrBuffer.HasValue && _skippingStderr)
        {
            throw new InvalidOperationException("Standard error is being skipped.");
        }
        _skippingStdout = !stdoutBuffer.HasValue;
        _skippingStderr = !stderrBuffer.HasValue;

        if (_readMode == ReadMode.Exited)
        {
            throw new InvalidOperationException("Process has exited");
        }

        var result = await ReadProcessAsync(stdoutBuffer, stderrBuffer, cancellationToken);
        return (result.Type, result.BytesRead);
    }

    public async ValueTask<(bool isError, int bytesRead)> ReadAsync(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken = default)
    {
        CheckReadMode(ReadMode.ReadBytes);

        while (true)
        {
            (ProcessReadType ReadType, int BytesRead) = await ReadCore(stdoutBuffer, stderrBuffer, cancellationToken).ConfigureAwait(false);
            switch (ReadType)
            {
                case ProcessReadType.StandardOutput:
                    return (false, BytesRead);
                case ProcessReadType.StandardError:
                    return (true, BytesRead);
                case ProcessReadType.ProcessExit:
                    _readMode = ReadMode.Exited;
                    return (false, 0);
                default:
                    throw new IndexOutOfRangeException($"Unexpected read type: {ReadType}.");
            }
        }
    }

    public ValueTask WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        return ReadToEndAsync(null, null, null, null, cancellationToken);
    }

    public async ValueTask<(string? stdout, string? stderr)> ReadToEndAsStringAsync(bool readStdout = true, bool readStderr = true, CancellationToken cancellationToken = default)
    {
        CheckReadMode(ReadMode.ReadChars);

        while (true)
        {
            ProcessReadType readType = await ReadCharsAsync(readStdout, readStderr, cancellationToken).ConfigureAwait(false); ;
            if (readType == ProcessReadType.ProcessExit)
            {
                _readMode = ReadMode.Exited;
                string? stdout = readStdout ? _stdoutBuffer.BuildString() : null;
                string? stderr = readStderr ? _stderrBuffer.BuildString() : null;
                return (stdout, stderr);
            }
        }
    }

    public async ValueTask ReadToEndAsync(Stream? stdoutStream, Stream? stderrStream, CancellationToken cancellationToken = default)
    {
        ReadMode readMode = stdoutStream is null && stderrStream is null ? ReadMode.Exited : ReadMode.ReadBytes;
        CheckReadMode(readMode);

        await ReadToEndAsync(stdoutStream != null ? writeToStream : null, stdoutStream,
                                stderrStream != null ? writeToStream : null, stderrStream,
                                cancellationToken).ConfigureAwait(false);

        if (stdoutStream != null)
        {
            await stdoutStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        if (stderrStream != null && stderrStream != stdoutStream)
        {
            await stderrStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        static async ValueTask writeToStream(Memory<byte> buffer, object? context, CancellationToken ct)
        {
            Stream stream = (Stream)context!;
            await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask ReadToEndAsync(Func<Memory<byte>, object?, CancellationToken, ValueTask>? handleStdout, object? stdoutContext,
                                            Func<Memory<byte>, object?, CancellationToken, ValueTask>? handleStderr, object? stderrContext,
                                            CancellationToken cancellationToken = default)
    {
        CheckReadMode(ReadMode.ReadBytes);

        bool readStdout = handleStdout != null;
        bool readStderr = handleStderr != null;
        byte[]? buffer = ArrayPool<byte>.Shared.Rent(4096);
        Memory<byte>? stdoutBuffer = readStdout ? buffer : default(Memory<byte>?);
        Memory<byte>? stderrBuffer = readStderr ? buffer : default(Memory<byte>?);

        try
        {
            do
            {
                (ProcessReadType readType, int bytesRead) = await ReadCore(stdoutBuffer, stderrBuffer, cancellationToken).ConfigureAwait(false); ;
                if (readType == ProcessReadType.StandardOutput)
                {
                    await handleStdout!(stdoutBuffer!.Value.Slice(0, bytesRead), stdoutContext, cancellationToken).ConfigureAwait(false);
                }
                else if (readType == ProcessReadType.StandardError)
                {
                    await handleStderr!(stderrBuffer!.Value.Slice(0, bytesRead), stderrContext, cancellationToken).ConfigureAwait(false);
                }
                else if (readType == ProcessReadType.ProcessExit)
                {
                    _readMode = ReadMode.Exited;
                    return;
                }
            } while (true);
        }
        catch
        {
            _readMode = ReadMode.ReadException;

            throw;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public async IAsyncEnumerable<(bool isError, string line)> ReadAllLinesAsync(bool readStdout = true, bool readStderr = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CheckReadMode(ReadMode.ReadChars);

        while (true)
        {
            (bool isError, string? line) = await ReadLineAsync(readStdout, readStderr, cancellationToken).ConfigureAwait(false); ;
            if (line == null)
            {
                break;
            }
            yield return (isError, line);
        }
    }

    public async ValueTask<(bool isError, string? line)> ReadLineAsync(bool readStdout = true, bool readStderr = true, CancellationToken cancellationToken = default)
    {
        CheckReadMode(ReadMode.ReadChars);

        string? line;
        if (readStdout && _stdoutBuffer.TryReadLine(out line, _delayedExit))
        {
            return (false, line);
        }
        if (readStderr && _stderrBuffer.TryReadLine(out line, _delayedExit))
        {
            return (true, line);
        }
        if (_delayedExit)
        {
            _readMode = ReadMode.Exited;
            return (false, null);
        }
        while (true)
        {
            ProcessReadType readType = await ReadCharsAsync(readStdout, readStderr, cancellationToken).ConfigureAwait(false); ;
            if (readType == ProcessReadType.StandardOutput)
            {
                if (_stdoutBuffer.TryReadLine(out line, false))
                {
                    return (false, line);
                }
            }
            else if (readType == ProcessReadType.StandardError)
            {
                if (_stderrBuffer.TryReadLine(out line, false))
                {
                    return (true, line);
                }
            }
            else if (readType == ProcessReadType.ProcessExit)
            {
                if (readStdout && _stdoutBuffer.TryReadLine(out line, true))
                {
                    _delayedExit = true;
                    return (false, line);
                }
                if (readStderr && _stderrBuffer.TryReadLine(out line, true))
                {
                    _delayedExit = true;
                    return (true, line);
                }
                _readMode = ReadMode.Exited;
                return (false, null);
            }
        }
    }

    private async ValueTask<ProcessReadType> ReadCharsAsync(bool readStdout, bool readStderr, CancellationToken cancellationToken)
    {
        if (_byteBuffer == null)
        {
            // TODO: alloc from ArrayPool?
            _byteBuffer = new byte[BufferSize];
            if (readStdout)
            {
                _stdoutBuffer.Initialize(_standardOutputEncoding);
            }
            if (readStderr)
            {
                _stderrBuffer.Initialize(_standardErrorEncoding);
            }
        }
        (ProcessReadType readType, int bytesRead) = await ReadCore(readStdout ? _byteBuffer : default(Memory<byte>?),
                                                                                readStderr ? _byteBuffer : default(Memory<byte>?), cancellationToken)
                                                                                .ConfigureAwait(false);
        switch (readType)
        {
            case ProcessReadType.StandardOutput:
                _stdoutBuffer.AppendFromEncoded(_byteBuffer.AsSpan(0, bytesRead));
                break;
            case ProcessReadType.StandardError:
                _stderrBuffer.AppendFromEncoded(_byteBuffer.AsSpan(0, bytesRead));
                break;
            case ProcessReadType.ProcessExit:
                break;
            default:
                throw new InvalidOperationException($"Unknown type: {readType}.");
        }

        return readType;
    }

    public virtual void Dispose()
    {
        _readMode = ReadMode.Disposed;
    }

    private void CheckReadMode(ReadMode readMode)
    {
        if (_readMode == ReadMode.Disposed)
        {
            throw new ObjectDisposedException(typeof(Process).FullName);
        }
        else if (_readMode == ReadMode.Exited)
        {
            throw new InvalidOperationException("The process has exited");
        }
        else if (_readMode == ReadMode.ReadException && readMode != ReadMode.Exited)
        {
            throw new InvalidOperationException("Previous read operation threw an exception.");
        }
        else if (_readMode == ReadMode.ReadChars && readMode == ReadMode.ReadBytes)
        {
            throw new InvalidOperationException("Cannot read raw bytes after reading chars.");
        }
        if (_readMode != ReadMode.Exited)
        {
            _readMode = readMode;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_readMode == ReadMode.Disposed)
        {
            ThrowObjectDisposedException();
        }
    }

    private void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException(typeof(Process).FullName);
    }

    sealed class StdInStream : Stream
    {
        private readonly Process _process;

        public StdInStream(Process process)
        {
            _process = process;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask; // WriteAsync always flushes.
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            // TODO: maybe wrap in IOException.
            return _process.WriteAsync(buffer, cancellationToken);
        }
    }
}