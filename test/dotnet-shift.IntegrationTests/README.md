# dotnet-shift.IntegrationTests

These tests run against an OpenShift cluster.
To run the tests, you must set the `TEST_CONTEXT` to a `kubectl`/`oc`/`dotnet-shift` context.

The tests provide additional output which is not visible by default when running `dotnet test`.
It can be printed to standard output by setting the `--logger` argument.

```
export TEST_CONTEXT=sandbox
dotnet test --logger "console;verbosity=detailed"
```
