# dotnet shift

An opinionated .NET cli tool for working with OpenShift.

## Usage

Install .NET 6 or .NET 7 using the instructions at https://learn.microsoft.com/en-us/dotnet/core/install/.

Install the tool:

```
$ dotnet tool update -g dotnet-shift --prerelease --add-source https://www.myget.org/F/tmds/api/v3/index.json
```

Create an app:
```
$ dotnet new web -o /tmp/web
```

To log into the OpenShift cluster copy the `oc login` command from the OpenShift Web Console and replace `oc` by `dotnet shift`. If you don't have an OpenShift cluster, you can create a free cluster for experimenting at https://developers.redhat.com/developer-sandbox.


```
$ dotnet shift login --token=xyz --server=https://abc.openshiftapps.com:6443
```

Deploy the application to the cluster:
```
$ dotnet shift deploy --expose /tmp/web
```

## .NET Project Configuration

### ContainerEnvironmentVariable

Adds an environment variable in the container.

**Example:**

```xml
<ItemGroup>
  <ContainerEnvironmentVariable Include="LOGGER_VERBOSITY" Value="Trace" />
</ItemGroup>
```

### Resource Requirements

The `ContainerCpuRequest`, `ContainerCpuLimit`, `ContainerMemoryRequest`, `ContainerMemoryLimit` properties can be used to configure cpu and memory requests and limits.

**Example:**

```xml
<PropertyGroup>
  <ContainerCpuRequest>0.5</ContainerCpuRequest>
  <ContainerCpuLimit>2</ContainerCpuLimit>
  <ContainerMemoryRequest>100M</ContainerMemoryRequest>
  <ContainerMemoryLimit>200M</ContainerMemoryLimit>
</PropertyGroup>
```

## ConfigMap

The application is deployed with a `ConfigMap` with the same name as the application.

The `ConfigMap` is prepopulated with an empty `appsettings.json` entry.

```yaml
kind: ConfigMap
apiVersion: v1
data:
  appsettings.json: |-
    {
    }
```

The `ConfigMap` is **not** overwritten when the application is re-deployed.

The `ConfigMap` is mounted in the container at `/config`.

The `appsettings.json` file can be added to the ASP.NET configuration by using the following code:

```cs
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("/config/appsettings.json", optional: true, reloadOnChange: true);
```

The `reloadOnChange: true` argument causes the application to pick up changes made to the `ConfigMap`
without requiring a restart.
