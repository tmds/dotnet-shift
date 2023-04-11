namespace OpenShift;

partial class OpenShiftClient : IOpenShiftClient
{
    public Task<Deployment?> GetDeploymentAsync(string name, CancellationToken cancellationToken)
        => ReadAppsV1NamespacedDeploymentAsync(name, Namespace, cancellationToken: cancellationToken);

    public Task<Deployment> CreateDeploymentAsync(Deployment deployment, CancellationToken cancellationToken)
        => CreateAppsV1NamespacedDeploymentAsync(deployment, Namespace, cancellationToken: cancellationToken);

    public Task<Deployment> PatchDeploymentAsync(Deployment deployment, CancellationToken cancellationToken)
        => PatchAppsV1NamespacedDeploymentAsync(deployment, deployment.Metadata.Name, Namespace, cancellationToken: cancellationToken);

    public Task<DeploymentList> ListDeploymentsAsync(string labelSelector, CancellationToken cancellationToken)
        => ListAppsV1NamespacedDeploymentAsync(Namespace, labelSelector: labelSelector, cancellationToken: cancellationToken);

    public Task DeleteDeploymentAsync(string name, CancellationToken cancellationToken)
        => DeleteAppsV1NamespacedDeploymentAsync(name, Namespace, cancellationToken: cancellationToken);

    public Task<Deployment> ReplaceDeploymentAsync(Deployment deployment, CancellationToken cancellationToken)
        => ReplaceAppsV1NamespacedDeploymentAsync(deployment, deployment.Metadata.Name, Namespace, cancellationToken: cancellationToken);

    private async System.Threading.Tasks.Task<Deployment> CreateAppsV1NamespacedDeploymentAsync(Deployment body, string @namespace, string? dryRun = null, string? fieldManager = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        if (body == null)
            throw new System.ArgumentNullException("body");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments?");
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (dryRun != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("dryRun") + "=").Append(System.Uri.EscapeDataString(ConvertToString(dryRun, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (fieldManager != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("fieldManager") + "=").Append(System.Uri.EscapeDataString(ConvertToString(fieldManager, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                var json_ = Newtonsoft.Json.JsonConvert.SerializeObject(body, _settings.Value);
                var content_ = new System.Net.Http.StringContent(json_);
                content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                request_.Content = content_;
                request_.Method = new System.Net.Http.HttpMethod("POST");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 201)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 202)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    private async System.Threading.Tasks.Task<Deployment> PatchAppsV1NamespacedDeploymentAsync(Deployment body, string name, string @namespace, string? dryRun = null, string? fieldManager = null, bool? force = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        if (body == null)
            throw new System.ArgumentNullException("body");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments/{name}?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (dryRun != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("dryRun") + "=").Append(System.Uri.EscapeDataString(ConvertToString(dryRun, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (fieldManager != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("fieldManager") + "=").Append(System.Uri.EscapeDataString(ConvertToString(fieldManager, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (force != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("force") + "=").Append(System.Uri.EscapeDataString(ConvertToString(force, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                var json_ = Newtonsoft.Json.JsonConvert.SerializeObject(body, _settings.Value);
                var content_ = new System.Net.Http.StringContent(json_);
                content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/strategic-merge-patch+json");
                request_.Content = content_;
                request_.Method = new System.Net.Http.HttpMethod("PATCH");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 201)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    private async System.Threading.Tasks.Task<Status> DeleteAppsV1NamespacedDeploymentAsync(string name, string @namespace, DeleteOptions? body = null, string? dryRun = null, int? gracePeriodSeconds = null, bool? orphanDependents = null, string? propagationPolicy = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments/{name}?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (dryRun != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("dryRun") + "=").Append(System.Uri.EscapeDataString(ConvertToString(dryRun, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (gracePeriodSeconds != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("gracePeriodSeconds") + "=").Append(System.Uri.EscapeDataString(ConvertToString(gracePeriodSeconds, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (orphanDependents != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("orphanDependents") + "=").Append(System.Uri.EscapeDataString(ConvertToString(orphanDependents, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (propagationPolicy != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("propagationPolicy") + "=").Append(System.Uri.EscapeDataString(ConvertToString(propagationPolicy, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                var json_ = Newtonsoft.Json.JsonConvert.SerializeObject(body, _settings.Value);
                var content_ = new System.Net.Http.StringContent(json_);
                content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                request_.Content = content_;
                request_.Method = new System.Net.Http.HttpMethod("DELETE");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Status>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 202)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Status>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    private async System.Threading.Tasks.Task<Deployment?> ReadAppsV1NamespacedDeploymentAsync(string name, string @namespace, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments/{name}?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                request_.Method = new System.Net.Http.HttpMethod("GET");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else if (status_ == 404)
                    {
                        return null;
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    private async System.Threading.Tasks.Task<DeploymentList> ListAppsV1NamespacedDeploymentAsync(string @namespace, bool? allowWatchBookmarks = null, string? @continue = null, string? fieldSelector = null, string? labelSelector = null, int? limit = null, string? resourceVersion = null, string? resourceVersionMatch = null, int? timeoutSeconds = null, bool? watch = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments?");
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (allowWatchBookmarks != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("allowWatchBookmarks") + "=").Append(System.Uri.EscapeDataString(ConvertToString(allowWatchBookmarks, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (@continue != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("continue") + "=").Append(System.Uri.EscapeDataString(ConvertToString(@continue, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (fieldSelector != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("fieldSelector") + "=").Append(System.Uri.EscapeDataString(ConvertToString(fieldSelector, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (labelSelector != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("labelSelector") + "=").Append(System.Uri.EscapeDataString(ConvertToString(labelSelector, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (limit != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("limit") + "=").Append(System.Uri.EscapeDataString(ConvertToString(limit, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (resourceVersion != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("resourceVersion") + "=").Append(System.Uri.EscapeDataString(ConvertToString(resourceVersion, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (resourceVersionMatch != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("resourceVersionMatch") + "=").Append(System.Uri.EscapeDataString(ConvertToString(resourceVersionMatch, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (timeoutSeconds != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("timeoutSeconds") + "=").Append(System.Uri.EscapeDataString(ConvertToString(timeoutSeconds, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (watch != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("watch") + "=").Append(System.Uri.EscapeDataString(ConvertToString(watch, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                request_.Method = new System.Net.Http.HttpMethod("GET");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<DeploymentList>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    private async System.Threading.Tasks.Task<Deployment> ReplaceAppsV1NamespacedDeploymentAsync(Deployment body, string name, string @namespace, string? dryRun = null, string? fieldManager = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        if (body == null)
            throw new System.ArgumentNullException("body");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/apps/v1/namespaces/{namespace}/deployments/{name}?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (dryRun != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("dryRun") + "=").Append(System.Uri.EscapeDataString(ConvertToString(dryRun, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (fieldManager != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("fieldManager") + "=").Append(System.Uri.EscapeDataString(ConvertToString(fieldManager, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                var json_ = Newtonsoft.Json.JsonConvert.SerializeObject(body, _settings.Value);
                var content_ = new System.Net.Http.StringContent(json_);
                content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                request_.Content = content_;
                request_.Method = new System.Net.Http.HttpMethod("PUT");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 201)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Deployment>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("Unauthorized", status_, responseText_, headers_, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw CreateApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }
}