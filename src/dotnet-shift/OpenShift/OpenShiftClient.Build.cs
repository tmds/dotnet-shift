using System.Net.Http;

namespace OpenShift;

partial class OpenShiftClient : IOpenShiftClient
{
    public Task<Build> StartBinaryBuildAsync(string buildConfigName, Stream archiveStream, CancellationToken cancellationToken)
        => ConnectBuildOpenshiftIoV1PostNamespacedBuildConfigInstantiatebinaryAsync(buildConfigName, Namespace, new StreamContent(archiveStream), cancellationToken: cancellationToken);

    public Task<Stream> FollowBuildLogAsync(string build, CancellationToken cancellationToken)
        => ReadBuildOpenshiftIoV1NamespacedBuildLogAsync(build, Namespace, follow: true, cancellationToken: cancellationToken);

    public Task<Build?> GetBuildAsync(string name, CancellationToken cancellationToken)
        => ReadBuildOpenshiftIoV1NamespacedBuildAsync(name, Namespace, cancellationToken: cancellationToken);

    private async Task<Build> ConnectBuildOpenshiftIoV1PostNamespacedBuildConfigInstantiatebinaryAsync(string name, string @namespace, HttpContent content, string? asFile = null, string? revision_authorEmail = null, string? revision_authorName = null, string? revision_commit = null, string? revision_committerEmail = null, string? revision_committerName = null, string? revision_message = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/build.openshift.io/v1/namespaces/{namespace}/buildconfigs/{name}/instantiatebinary?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (asFile != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("asFile") + "=").Append(System.Uri.EscapeDataString(ConvertToString(asFile, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_authorEmail != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.authorEmail") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_authorEmail, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_authorName != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.authorName") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_authorName, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_commit != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.commit") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_commit, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_committerEmail != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.committerEmail") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_committerEmail, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_committerName != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.committerName") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_committerName, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (revision_message != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("revision.message") + "=").Append(System.Uri.EscapeDataString(ConvertToString(revision_message, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        urlBuilder_.Length--;

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new System.Net.Http.HttpRequestMessage())
            {
                request_.Content = content;
                request_.Method = new System.Net.Http.HttpMethod("POST");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("*/*"));

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
                    if (status_ == 201)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<Build>(response_, headers_, cancellationToken).ConfigureAwait(false);
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

    private async Task<Stream> ReadBuildOpenshiftIoV1NamespacedBuildLogAsync(string name, string @namespace, string? container = null, bool? follow = null, bool? insecureSkipTLSVerifyBackend = null, int? limitBytes = null, bool? nowait = null, string? pretty = null, bool? previous = null, int? sinceSeconds = null, int? tailLines = null, bool? timestamps = null, int? version = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/build.openshift.io/v1/namespaces/{namespace}/builds/{name}/log?");
        urlBuilder_.Replace("{name}", System.Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{namespace}", System.Uri.EscapeDataString(ConvertToString(@namespace, System.Globalization.CultureInfo.InvariantCulture)));
        if (container != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("container") + "=").Append(System.Uri.EscapeDataString(ConvertToString(container, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (follow != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("follow") + "=").Append(System.Uri.EscapeDataString(ConvertToString(follow, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (insecureSkipTLSVerifyBackend != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("insecureSkipTLSVerifyBackend") + "=").Append(System.Uri.EscapeDataString(ConvertToString(insecureSkipTLSVerifyBackend, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (limitBytes != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("limitBytes") + "=").Append(System.Uri.EscapeDataString(ConvertToString(limitBytes, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (nowait != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("nowait") + "=").Append(System.Uri.EscapeDataString(ConvertToString(nowait, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (pretty != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("pretty") + "=").Append(System.Uri.EscapeDataString(ConvertToString(pretty, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (previous != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("previous") + "=").Append(System.Uri.EscapeDataString(ConvertToString(previous, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (sinceSeconds != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("sinceSeconds") + "=").Append(System.Uri.EscapeDataString(ConvertToString(sinceSeconds, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (tailLines != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("tailLines") + "=").Append(System.Uri.EscapeDataString(ConvertToString(tailLines, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (timestamps != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("timestamps") + "=").Append(System.Uri.EscapeDataString(ConvertToString(timestamps, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
        }
        if (version != null)
        {
            urlBuilder_.Append(System.Uri.EscapeDataString("version") + "=").Append(System.Uri.EscapeDataString(ConvertToString(version, System.Globalization.CultureInfo.InvariantCulture))).Append("&");
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
                        disposeResponse_ = false;
                        return response_.Content!.ReadAsStream();
                        // var objectResponse_ = await ReadObjectResponseAsync<BuildLog>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        // if (objectResponse_.Object == null)
                        // {
                        //     throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        // }
                        // return objectResponse_.Object;
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

    private async Task<Build?> ReadBuildOpenshiftIoV1NamespacedBuildAsync(string name, string @namespace, string? pretty = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        if (name == null)
            throw new System.ArgumentNullException("name");

        if (@namespace == null)
            throw new System.ArgumentNullException("@namespace");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/apis/build.openshift.io/v1/namespaces/{namespace}/builds/{name}?");
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
                        var objectResponse_ = await ReadObjectResponseAsync<Build>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw CreateApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        string responseText_ = ( response_.Content == null ) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
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
}