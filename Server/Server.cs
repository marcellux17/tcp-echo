using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Helpers;

namespace Server
{
    internal class Server
    {
        int heartBeatIntervalInSec = 5;
        int heartBeatChecksLimit = 3;

        TcpListener listener;
        ConcurrentDictionary<TcpClient, DateTime> clients;
        ConcurrentDictionary<TcpClient, SemaphoreSlim> writeLocks;

        public Server(Int32 port)
        {
            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, port);
            listener = new TcpListener(serverEndPoint);
            clients = new ConcurrentDictionary<TcpClient, DateTime>();
            writeLocks = new ConcurrentDictionary<TcpClient, SemaphoreSlim>();
        }
        public async Task Start()
        {
            listener.Start();
            while (true)
            {
                TcpClient newClient = await listener.AcceptTcpClientAsync();
                Console.WriteLine($"Client accepted: {newClient.Client.RemoteEndPoint?.ToString()}");
                HandleNewClient(newClient);
            }
        }
        private async Task HandleNewClient(TcpClient client)
        {
            clients[client] = DateTime.UtcNow;
            writeLocks[client] = new SemaphoreSlim(1, 1);
            try
            {
                StartHeartBeatForClient(client);
                while (true)
                {
                    int messageType = await NetworkHelper.GetMessageType(client);
                    clients[client] =  DateTime.UtcNow;
                    int payloadSize = await NetworkHelper.GetMessageLength(client);
                    string message = await NetworkHelper.GetMessage(client, payloadSize);
                    if (messageType == 0)
                    {

                        string response = String.Format($"Server has seen your message: {message}");

                        await SendMessageToClient(client, 0, response);
                    }
                   
                }
            }
            catch (Exception e)
            {
                CloseClient(client);
            }
        }
        private async Task StartHeartBeatForClient(TcpClient client)
        {
            int heartBeatChecksLeft = heartBeatChecksLimit;
            bool everythingOk = true;
            while (everythingOk)
            {
                DateTime latest;
                bool success = clients.TryGetValue(client, out latest);

                heartBeatChecksLeft--;
                if (success)
                {
                    if ((DateTime.UtcNow - latest).TotalSeconds <= heartBeatIntervalInSec * 2.5 && heartBeatChecksLeft >= 0)
                    {
                        heartBeatChecksLeft = heartBeatChecksLimit;
                        await SendMessageToClient(client, 1, "PING");

                    }
                    else if (heartBeatChecksLeft >= 0)
                    {
                        await SendMessageToClient(client, 1, "PING");

                    }
                    else
                    {
                        CloseClient(client);
                        everythingOk = false;
                    }
                }
                else
                {
                    everythingOk = false;
                }

                if (everythingOk)
                {
                    await Task.Delay(heartBeatIntervalInSec * 1000);
                }


            }

        }
        private void CloseClient(TcpClient client)
        {
            bool success = clients.TryRemove(client, out _);
            if (success)
            {
                writeLocks.TryRemove(client, out _);
                Console.WriteLine($"Client disconnected: {client.Client.RemoteEndPoint?.ToString()}");
                client.Close();
            }
        }
        private async Task SendMessageToClient(TcpClient client,int messageType, string message)
        {
            bool success = writeLocks.TryGetValue(client, out var writeLock);
            if (success)
            {
                await writeLock!.WaitAsync();
                try
                {
                    await NetworkHelper.SendMessage(messageType, client, message);
                }
                finally
                {
                    writeLock.Release();
                }
            }
        }
    }
}
