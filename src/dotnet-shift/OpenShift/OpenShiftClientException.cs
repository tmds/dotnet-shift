using System.Net;
using System.Net.Sockets;

namespace OpenShift;



class OpenShiftClientException : System.Exception
{
    internal OpenShiftClientException(string host, string message, OpenShiftClientExceptionCause cause, HttpStatusCode? httpStatusCode, System.Exception? innerException) :
        base(message, innerException)
    {
        Host = host;
        Cause = cause;
        HttpStatusCode = httpStatusCode;
    }

    public string Host { get; }

    public OpenShiftClientExceptionCause Cause { get; }

    public HttpStatusCode? HttpStatusCode { get; }

    public SocketError? SocketError => (InnerException?.InnerException as SocketException)?.SocketErrorCode;
}
