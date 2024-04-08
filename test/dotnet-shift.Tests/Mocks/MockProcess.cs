using System.Text;

#pragma warning disable CS1998 // This async method lacks 'await' operators

sealed partial class MockProcess : Process
{
    private readonly int _exitCode;
    private readonly byte[] _stdout;
    private readonly byte[] _stderr;
    private int _stdoutOffset;
    private int _stderrOffset;

    public MockProcess(int exitCode = 0, string? stdout = null, string? stderr = null)
    {
        _exitCode = exitCode;
        _stdout = Encoding.UTF8.GetBytes(stdout ?? "");
        _stderr = Encoding.UTF8.GetBytes(stderr ?? "");
    }

    private protected async override ValueTask<ProcessReadResult> ReadProcessAsync(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken = default)
    {
        if (_stdoutOffset < _stdout.Length)
        {
            if (stdoutBuffer.HasValue)
            {
                int bytesRead = Math.Min(stdoutBuffer.Value.Length, _stdout.Length - _stdoutOffset);
                _stdout.AsSpan(_stdoutOffset, bytesRead).CopyTo(stdoutBuffer.Value.Span);
                _stdoutOffset += bytesRead;
                return new ProcessReadResult(ProcessReadType.StandardOutput, bytesRead);
            }
            else
            {
                _stdoutOffset = _stdout.Length;
            }
        }
        if (_stderrOffset < _stderr.Length)
        {
            if (stderrBuffer.HasValue)
            {
                int bytesRead = Math.Min(stderrBuffer.Value.Length, _stderr.Length - _stderrOffset);
                _stderr.AsSpan(_stderrOffset, bytesRead).CopyTo(stderrBuffer.Value.Span);
                _stderrOffset += bytesRead;
                return new ProcessReadResult(ProcessReadType.StandardError, bytesRead);
            }
            else
            {
                _stderrOffset = _stderr.Length;
            }
        }
        SetExitCode(_exitCode);
        return new ProcessReadResult(ProcessReadType.ProcessExit);
    }

    private protected override ValueTask WriteStandardInputAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return default;
    }
}