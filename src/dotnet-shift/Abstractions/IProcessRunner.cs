
interface IProcessRunner
{
    Process Run(string filename, IEnumerable<string> args, IDictionary<string, string?>? envvars);
}

sealed class ProcessRunner : IProcessRunner
{
    public Process Run(string filename, IEnumerable<string> arguments, IDictionary<string, string?>? envvars)
    {
        System.Diagnostics.Process process = new();
        process.StartInfo.FileName = filename;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (envvars is not null)
        {
            foreach (var envvar in envvars)
            {
                if (envvar.Value is null)
                {
                    process.StartInfo.EnvironmentVariables.Remove(envvar.Key);
                }
                else
                {
                    process.StartInfo.EnvironmentVariables[envvar.Key] = envvar.Value;
                }
            }
        }

        process.Start();

        return new ProcessRunnerProcess(process);
    }
}

sealed class ProcessRunnerProcess : Process
{
    private readonly System.Diagnostics.Process _process;
    private readonly byte[] _stdoutBuffer = new byte[4096];
    private readonly byte[] _stderrBuffer = new byte[4096];
    private int _stdoutBufferLength = 0;
    private int _stderrBufferLength = 0;
    private int _stdoutBufferConsumed = 0;
    private int _stderrBufferConsumed = 0;
    private bool _stdoutEof = false;
    private bool _stderrEof = false;
    private Task<int>? _stdoutReader;
    private Task<int>? _stderrReader;

    public ProcessRunnerProcess(System.Diagnostics.Process process)
    {
        _process = process;
    }

    private protected async override ValueTask<ProcessReadResult> ReadProcessAsync(Memory<byte>? stdoutBuffer, Memory<byte>? stderrBuffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_stdoutReader?.IsCompleted == true)
            {
                _stdoutBufferLength = await _stdoutReader;
                _stdoutEof = _stdoutBufferLength == 0;
                _stdoutBufferConsumed = 0;
                _stdoutReader = null;
            }
            if (_stderrReader?.IsCompleted == true)
            {
                _stderrBufferLength = await _stderrReader;
                _stderrEof = _stderrBufferLength == 0;
                _stderrBufferConsumed = 0;
                _stderrReader = null;
            }
            int stdoutRemaining = _stdoutBufferLength - _stdoutBufferConsumed;
            if (stdoutRemaining > 0)
            {
                if (stdoutBuffer.HasValue)
                {
                    int bytesRead = Math.Min(stdoutBuffer.Value.Length, stdoutRemaining);
                    _stdoutBuffer.AsSpan(_stdoutBufferConsumed, bytesRead).CopyTo(stdoutBuffer.Value.Span);
                    _stdoutBufferConsumed += bytesRead;
                    return new ProcessReadResult(ProcessReadType.StandardOutput, bytesRead);
                }
                else
                {
                    _stdoutBufferLength = 0;
                }
            }
            int stderrRemaining = _stderrBufferLength - _stderrBufferConsumed;
            if (stderrRemaining > 0)
            {
                if (stderrBuffer.HasValue)
                {
                    int bytesRead = Math.Min(stderrBuffer.Value.Length, stderrRemaining);
                    _stderrBuffer.AsSpan(_stderrBufferConsumed, bytesRead).CopyTo(stderrBuffer.Value.Span);
                    _stderrBufferConsumed += bytesRead;
                    return new ProcessReadResult(ProcessReadType.StandardError, bytesRead);
                }
                else
                {
                    _stderrBufferLength = 0;
                }
            }
            if (_stdoutReader is null && !_stdoutEof)
            {
                _stdoutReader = _process.StandardOutput.BaseStream.ReadAsync(_stdoutBuffer).AsTask();
            }
            if (_stderrReader is null && !_stderrEof)
            {
                _stderrReader = _process.StandardError.BaseStream.ReadAsync(_stderrBuffer).AsTask();
            }
            if (_stdoutEof && _stderrEof)
            {
                await _process.WaitForExitAsync(cancellationToken);
                SetExitCode(_process.ExitCode);
                return new ProcessReadResult(ProcessReadType.ProcessExit);
            }
            else if (_stdoutEof)
            {
                await _stderrReader!.WaitAsync(cancellationToken);
            }
            else if (_stderrEof)
            {
                await _stdoutReader!.WaitAsync(cancellationToken);
            }
            else
            {
                await Task.WhenAny([_stdoutReader!, _stderrReader!]).WaitAsync(cancellationToken);
            }
        }
    }

    private protected async override ValueTask WriteStandardInputAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _process.StandardInput.BaseStream.WriteAsync(buffer, cancellationToken);
    }
}