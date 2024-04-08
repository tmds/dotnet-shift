sealed class MockProcessRunner : IProcessRunner
{
    public delegate Process RunProcess(string filename, IEnumerable<string> args, IDictionary<string, string?>? envvars);

    private readonly RunProcess _runProcess;

    public MockProcessRunner(RunProcess runProcess)
    {
        _runProcess = runProcess;
    }

    public Process Run(string filename, IEnumerable<string> args, IDictionary<string, string?>? envvars)
        => _runProcess(filename, args, envvars);
}