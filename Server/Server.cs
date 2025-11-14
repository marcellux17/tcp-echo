using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Helpers;

namespace Server
{
    internal class Server
    {
        const int HEARTBEAT_INTERVAL_IN_SEC = 5;
        const int HEARBEAT_CHECKS_LIMIT = 3;

        readonly TcpListener _listener;
        readonly ConcurrentDictionary<TcpClient, DateTime> _clients;
        readonly ConcurrentDictionary<TcpClient, SemaphoreSlim> _writeLocks;

        public Server(Int32 port)
        {
            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, port);
            _listener = new TcpListener(serverEndPoint);
            _clients = new ConcurrentDictionary<TcpClient, DateTime>();
            _writeLocks = new ConcurrentDictionary<TcpClient, SemaphoreSlim>();
        }
        public async Task Start()
        {
            _listener.Start();
            while (true)
            {
                TcpClient newClient = await _listener.AcceptTcpClientAsync();
                Console.WriteLine($"Client accepted: {newClient.Client.RemoteEndPoint?.ToString()}");
                HandleNewClient(newClient);
            }
        }
        async Task HandleNewClient(TcpClient client)
        {
            _clients[client] = DateTime.UtcNow;
            _writeLocks[client] = new SemaphoreSlim(1, 1);
            try
            {
                StartHeartBeatForClient(client);
                while (true)
                {
                    int messageType = await NetworkHelper.GetMessageType(client);
                    _clients[client] =  DateTime.UtcNow;
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
        async Task StartHeartBeatForClient(TcpClient client)
        {
            int heartBeatChecksLeft = HEARBEAT_CHECKS_LIMIT;
            bool everythingOk = true;
            while (everythingOk)
            {
                DateTime latest;
                bool success = _clients.TryGetValue(client, out latest);

                heartBeatChecksLeft--;
                if (success)
                {
                    if ((DateTime.UtcNow - latest).TotalSeconds <= HEARTBEAT_INTERVAL_IN_SEC * 2.5 && heartBeatChecksLeft >= 0)
                    {
                        heartBeatChecksLeft = HEARBEAT_CHECKS_LIMIT;
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
                    await Task.Delay(HEARTBEAT_INTERVAL_IN_SEC * 1000);
                }


            }

        }
        void CloseClient(TcpClient client)
        {
            bool success = _clients.TryRemove(client, out _);
            if (success)
            {
                _writeLocks.TryRemove(client, out _);
                Console.WriteLine($"Client disconnected: {client.Client.RemoteEndPoint?.ToString()}");
                client.Close();
            }
        }
        async Task SendMessageToClient(TcpClient client,int messageType, string message)
        {
            bool success = _writeLocks.TryGetValue(client, out var writeLock);
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
