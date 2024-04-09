# dotnet shift

An opinionated .NET cli tool for working with OpenShift.

## Usage

Install .NET 8 using the instructions at https://learn.microsoft.com/en-us/dotnet/core/install/.

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

### Environment Variables

`ContainerEnvironmentVariable` adds an environment variable in the **container image**.

**Example:**

```xml
<ItemGroup>
  <ContainerEnvironmentVariable Include="LOGGER_VERBOSITY" Value="Trace" />
</ItemGroup>
```

`K8sEnvironmentVariable` adds an environment variable in the **container deployment**.

**Example:**

```xml
<ItemGroup>
  <K8sEnvironmentVariable Include="LOGGER_VERBOSITY" Value="Trace" />
</ItemGroup>
```

### Resource Requirements

The `K8sCpuRequest`, `K8sCpuLimit`, `K8sMemoryRequest`, `K8sMemoryLimit` properties can be used to configure cpu and memory requests and limits.

**Example:**

```xml
<PropertyGroup>
  <K8sCpuRequest>0.5</K8sCpuRequest>
  <K8sCpuLimit>2</K8sCpuLimit>
  <K8sMemoryRequest>100M</K8sMemoryRequest>
  <K8sMemoryLimit>200M</K8sMemoryLimit>
</PropertyGroup>
```

### Ports

When an `ContainerEnvironmentVariable` for `ASPNETCORE_URLS` is set, ports are derived from it.
When publishing an ASP.NET Core project, and the environment variable is not explicitly set, a value of `http://*:8080` is used.

Additional ports can be exposed on the container using `ContainerPort`.

**Example:**

```xml
<ContainerPort Include="8081" Type="tcp"
               [ IsServicePort="true" Name="web2" ] />
```

To make a `ContainerPort` available through the service, you must add `IsServicePort = true` and set a `Name` for the port.

Ports from `ASPNETCORE_URLS` may be declared as `ContainerPort` to explicity set their `Name` and `IsServicePort` values.
The default value for an `ASPNETCORE_URLS` port `Name` is its scheme (e.g. `http`), and for its `IsServicePort` it is `true`.


### Persistent Storage

Persistent volume claims can be configured using `K8sPersistentStorage`.

`Access` can be set to `ReadWriteOnce`, `ReadOnlyMany`, `ReadWriteMany`, `ReadWriteOncePod`.
The default is `ReadWriteOnce`. When `Access` is `ReadWriteOnce` or `ReadWriteOncePod` the deployment will use the recreate strategy to ensure the previous pod is terminated so the new pod can attach.

**Example:**

```xml
<K8sPersistentStorage Include="data" Size="300Mi" Path="/data"
                            [ Limit="1Gi" StorageClass="myclass" Access="ReadWriteMany" ]/>
```

### ConfigMap

Config maps can be configured using `K8sConfigMap`.

**Example:**

```xml
<K8sConfigMap Include="config" Path="/config"
                    [ ReadOnly="true" ]/>
```

An `appsettings.json` file can be loaded from the above config map using the following code:

```cs
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("/config/appsettings.json", optional: true, reloadOnChange: true);
```

The `reloadOnChange: true` argument causes the application to pick up changes made to the `ConfigMap` without requiring a restart.

### Probes

Health, readiness and startup probes can be added through the project file. The probe path is required.
Other configuration is optional. The port defaults to `http` and its value must match a container port name.

**Example:**

```xml
<K8sLivenessPath>/healthz</K8sLivenessPath>
[
<K8sLivenessPort>http</K8sLivenessPort>
<K8sLivenessInitialDelay>0</K8sLivenessInitialDelay>
<K8sLivenessPeriod>10</K8sLivenessPeriod>
<K8sLivenessTimeout>1</K8sLivenessTimeout>
<K8sLivenessFailureThresholdCount>3</K8sLivenessFailureThresholdCount>
]

<K8sStartupPath>/startup-probe</K8sStartupPath>
[
<K8sStartupPort>http</K8sStartupPort>
<K8sStartupInitialDelay>0</K8sStartupInitialDelay>
<K8sStartupPeriod>10</K8sStartupPeriod>
<K8sStartupTimeout>1</K8sStartupTimeout>
<K8sStartupFailureThresholdCount>3</K8sStartupFailureThresholdCount>
]

<K8sReadinessPath>/readiness-probe</K8sReadinessPath>
[
<K8sReadinessPort>http</K8sReadinessPort>
<K8sReadinessInitialDelay>0</K8sReadinessInitialDelay>
<K8sReadinessPeriod>10</K8sReadinessPeriod>
<K8sReadinessTimeout>1</K8sReadinessTimeout>
<K8sReadinessFailureThresholdCount>3</K8sReadinessFailureThresholdCount>
]
```

### Deployment strategy

By default the application is deployed using a _rolling update_. This can be changed to _recreate_ by setting the `K8sDeploymentStrategy`.

Valid values are: `RollingUpdate`/`Recreate`. When empty, the tooling prefers `RollingUpdate` unless the project has some configuration that requires `Recreate`, like volumes that may can only be mounted once.

**Example:**

```xml
<K8sDeploymentStrategy>Recreate</K8sDeploymentStrategy>
```

### Deployment trigger

By default, when a new image is pushed to the .NET application ImageStream, the Kubernetes Deployment is updated automatically to use the new image.

This behavior can be disabled by adding an `K8sEnableImageStreamTagDeploymentTrigger` property and setting it to `false`.

By setting the property to `false`, the Deployment is update to use the new image in sync with other changes to the Deployment.

**Example:**

```xml
<K8sEnableImageStreamTagDeploymentTrigger>false</K8sEnableImageStreamTagDeploymentTrigger>
```

## Tekton

The `dotnet-shift-deploy` Tekton Task takes similar arguments as the `dotnet shift deploy` command.

To use the Tekton Task, add the .NET image streams and the `dotnet-shift-deploy` Tekton Task to your project.

```
oc apply -f https://raw.githubusercontent.com/redhat-developer/s2i-dotnetcore/main/dotnet_imagestreams.json
oc apply -f https://raw.githubusercontent.com/tmds/dotnet-shift/main/tekton/dotnet-shift-deploy-task.yaml
```
