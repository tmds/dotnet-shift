namespace OpenShift;

partial class OpenShiftClient : IOpenShiftClient
{
    public Task<SecretList> ListSecretsAsync(string? labelSelector, CancellationToken cancellationToken)
        => ListCoreV1NamespacedSecretAsync(Namespace, labelSelector: labelSelector, cancellationToken: cancellationToken);

    public Task<SecretList> ListSecretsAsync(string? labelSelector, string? fieldSelector, CancellationToken cancellationToken)
        => ListCoreV1NamespacedSecretAsync(Namespace, labelSelector: labelSelector, fieldSelector: fieldSelector, cancellationToken: cancellationToken);

    private async System.Threading.Tasks.Task<SecretList> ListCoreV1NamespacedSecretAsync(string @namespace, bool? allowWatchBookmarks = null, string? @continue = null, string? fieldSelector = null, string? labelSelector = null, int? limit = null, string? resourceVersion = null, string? resourceVersionMatch = null, int? timeoutSeconds = null, bool? watch = null, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/api/v1/namespaces/{namespace}/secrets?");
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
                        var objectResponse_ = await ReadObjectResponseAsync<SecretList>(response_, headers_, cancellationToken).ConfigureAwait(false);
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