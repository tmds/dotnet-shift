using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace OpenShift;

class OpenShiftClientException : Exception
{
    internal OpenShiftClientException(string host, string message, OpenShiftClientExceptionCause cause, HttpStatusCode? httpStatusCode, string? responseText, Exception? innerException) :
        base(message, innerException)
    {
        Host = host;
        Cause = cause;
        HttpStatusCode = httpStatusCode;
        ResponseText = responseText;
    }

    public string Host { get; }

    public OpenShiftClientExceptionCause Cause { get; }

    public HttpStatusCode? HttpStatusCode { get; }

    public SocketError? SocketError => SocketException?.SocketErrorCode;

    public string? ResponseText { get; }

    public SocketException? SocketException => FindInnerException<SocketException>();

    public AuthenticationException? AuthenticationException => FindInnerException<AuthenticationException>();

    private T? FindInnerException<T>() where T: Exception
    {
        Exception? ex = InnerException;
        while (ex is not null)
        {
            if (ex is T value)
            {
                return value;
            }
            ex = ex.InnerException;
        }
        return null;
    }
}
