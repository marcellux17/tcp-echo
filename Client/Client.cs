using Helpers;
using System.Net;
using System.Net.Sockets;

namespace Client
{
    internal class Client
    {
        const int HEARTBEAT_INTERVAL_IN_SEC = 5;
        const int HEARTBEAT_CHECKS_LIMIT = 3;
        TaskCompletionSource<string>? _messageEcho;
        DateTime _lastMessage;
        TcpClient _socket;
        IPEndPoint _remoteEndPoint;
        int _connectionAliveFlag;

        SemaphoreSlim writeLock;
        public Client(IPAddress serverAddress, Int32 serverPort)
        {
            _remoteEndPoint = new IPEndPoint(serverAddress, serverPort);
            _socket = new TcpClient(new IPEndPoint(IPAddress.Any, 0));
            _connectionAliveFlag = 0;
            writeLock = new SemaphoreSlim(1, 1);   
        }
        public async Task Connect()
        {
            try
            {
                _socket.Connect(_remoteEndPoint);
                _connectionAliveFlag = 1;
                ReadStream();
                PrintWelcomeMessage();
                CheckHeartBeats();
                while (Interlocked.CompareExchange(ref _connectionAliveFlag, 1, 1) == 1)
                {
                    Console.Write("> ");

                    string messageText = Console.ReadLine();
                    await writeLock.WaitAsync();
                    try
                    {
                        await NetworkHelper.SendMessage(0, _socket, messageText);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                    _messageEcho = new TaskCompletionSource<string>();
                    string echo = await _messageEcho.Task;
                    Console.WriteLine(echo);
                    await Task.Delay(100);

                }
            }
            catch (Exception ex)
            {
                await CloseClient();
            }

        }
        async Task CheckHeartBeats()
        {
            int heartBeatChecksLeft = HEARTBEAT_CHECKS_LIMIT;
            while (Interlocked.CompareExchange(ref _connectionAliveFlag, 1, 1) == 1)
            {
                heartBeatChecksLeft--;
                if ((DateTime.UtcNow - _lastMessage).TotalSeconds <= HEARTBEAT_INTERVAL_IN_SEC * 2.5 && heartBeatChecksLeft >= 0)
                {
                    heartBeatChecksLeft = HEARTBEAT_CHECKS_LIMIT;
                }
                else if (heartBeatChecksLeft < 0)
                {
                    await CloseClient();
                }
                if (Interlocked.CompareExchange(ref _connectionAliveFlag, 1, 1) == 1)
                {
                    await Task.Delay(HEARTBEAT_INTERVAL_IN_SEC * 1000);
                }
            }
        }
        async Task ReadStream()
        {
            while (true)
            {
                int messageType = await NetworkHelper.GetMessageType(_socket);
                _lastMessage = DateTime.UtcNow;
                int payloadLength = await NetworkHelper.GetMessageLength(_socket);
                string message = await NetworkHelper.GetMessage(_socket, payloadLength);
                if (messageType == 1)
                {
                    await writeLock.WaitAsync();
                    try
                    {
                        await NetworkHelper.SendMessage(1, _socket, "PONG");
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
                else
                {
                    _messageEcho.SetResult(message);
                }
            }

        }
        void PrintWelcomeMessage()
        {
            Console.WriteLine("Welcome to the echo server!");
            Console.WriteLine("Send messages to the server and see if it responds!");
        }
        async Task CloseClient()
        {
            if (Interlocked.Exchange(ref _connectionAliveFlag, 0) == 1)
            {
                Console.WriteLine($"Server unavailble :(");
                Console.WriteLine("Client shutting down...");
                _socket.Close();
                await Task.Delay(500);
                Environment.Exit(0);
            }
            
        }
    }
}
