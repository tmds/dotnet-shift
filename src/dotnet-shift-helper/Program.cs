using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

class Program
{
    private const int LocalPort = 5000;
    private const int RemotePort = 5000;
    private const string RemoteHost = "image-registry.openshift-image-registry.svc";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            TcpListener listener = new TcpListener(IPAddress.Any, LocalPort);
            listener.Start();
            Console.WriteLine($"Port forwarder started on port {LocalPort}. Forwarding connections to {RemoteHost}:{RemotePort}...");

            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                _ = ForwardTcpTraffic(client);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");

            return 1;
        }
    }

    private static async Task ForwardTcpTraffic(TcpClient client)
    {
        try
        {
            using (TcpClient remoteClient = new TcpClient())
            {
                await remoteClient.ConnectAsync(RemoteHost, RemotePort);

                using (NetworkStream localStream = client.GetStream())
                using (NetworkStream remoteStream = remoteClient.GetStream())
                {
                    await Task.WhenAny(
                        localStream.CopyToAsync(remoteStream),
                        remoteStream.CopyToAsync(localStream)
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error forwarding traffic: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }
}
