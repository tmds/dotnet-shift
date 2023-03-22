namespace OpenShift;

enum OpenShiftClientExceptionCause
{
    // There was an issue sending the request or receiving the response.
    ConnectionIssue,
    // The response body has unexpected content.
    UnexpectedResponseContent,
    // The response status indicates a failure.
    Failed
}