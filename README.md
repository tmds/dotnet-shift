# dotnet shift

An opioninated .NET cli tool for deploying .NET apps to OpenShift.

# Usage

Install .NET 7 using the instructions at https://learn.microsoft.com/en-us/dotnet/core/install/.

Install the tool:

```
$ dotnet tool install -g dotnet-shift --prerelease --add-source https://www.myget.org/F/tmds/api/v3/index.json
```

Create an app:
```
$ dotnet new web -o /tmp/web
$ cd /tmp/web
```

To log into the OpenShift cluster copy the `oc login` command from the OpenShift Web Console and replace `oc` by `dotnet shift`. If you don't have an OpenShift cluster, you can create a free cluster for experimenting at https://developers.redhat.com/developer-sandbox.


```
$ dotnet shift login --token=xyz --server=https://abc.openshiftapps.com:6443
```

Deploy the application to the cluster:
```
$ dotnet shift deploy
```

In the OpenShift Web Console, find the app you've deployed and open it in your browser.
