using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreWebsockets.Tests
{
    [TestClass]
    public class WebsocketTests
    {
        public class TestClass
        {
            public string Name { get; set; }
            public int Number { get; set; }
        }

        [TestMethod]
        public async Task TestWebsocketClient()
        {
            var url = "wss://echo.websocket.org";

            using var client = new WebSocketClient();

            var dataReceived = false;

            client.MessageReceived += (wsClient, data) =>
            {
                Console.Write(data);
                dataReceived = true;
            };

            client.ContinuationFrameReceived += (wsClient, data) =>
            {
                Console.Write(data);
            };

            client.ConnectionClosed += (wsClient, code) =>
            {
                Console.WriteLine($"Closed: {code}");
            };

            Assert.IsTrue(await client.Connect(url).ConfigureAwait(false));
            Thread.Sleep(1000);
            Assert.IsTrue(client.UpgradedConnection);

            Task.Run(() => client.Run());

            //client.Send("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.");

            var messageLength = 1024;
            await client.Send(string.Join("", Enumerable.Range(0, messageLength).Select(a => (a % 10 == 0) ? " " : "a"))).ConfigureAwait(false);

            while (!dataReceived && client.Connected)
                Thread.Sleep(100);

            Thread.Sleep(500);

            Assert.IsTrue(dataReceived);
        }

        [TestMethod]
        public async Task TestWebsocketMultipleMessages()
        {
            var port = 15005;

            using (var server = new SyncWebSocketServer(port))
            {
                server.ClientConnected += ClientConnected;
                Task.Run(() => server.Run());

                Thread.Sleep(2000);

                await TestClient(server, port, 0, 100).ConfigureAwait(false);
            }
        }

        [TestMethod]
        public void TestWebsocket()
        {
            var port = 15005;

            using (var server = new SyncWebSocketServer(port))
            {
                server.ClientConnected += ClientConnected;
                Task.Run(() => server.Run());

                Thread.Sleep(2000);

                TestClient(server, port, 0);

                Parallel.For(1, 3, (i) =>
                {
                    TestClient(server, port, i, 2);
                });
            }
        }

        int ConnectedClients = 0;

        private void ClientConnected(object sender, WebSocketClient e)
        {
            ConnectedClients++;

            Console.WriteLine($"Client Connected: {ConnectedClients}");
        }

        public async Task TestClient(SyncWebSocketServer server, int port, int index, int packets = 1)
        {
            using (var client = new WebSocketClient())
            {
                Assert.IsTrue(await client.Connect($"ws://127.0.0.1:{port}").ConfigureAwait(false));
                Thread.Sleep(1000);
                Assert.IsTrue(client.UpgradedConnection);

                var dataReceived = 0;
                var sent = 0;

                client.MessageReceived += (wsClient, data) =>
                {
                    Console.WriteLine($"{index}: {data}");
                    dataReceived++;
                };

                client.ConnectionClosed += (wsClient, reason) =>
                {
                    Console.WriteLine($"{index}: Close Connection - {reason}");
                };

                client.Pong += (wsClient, data) =>
                {
                    Console.WriteLine("PONG!");
                };

                Task.Run(() => client.Run());

                while (dataReceived < packets && client.Connected && server.Clients.Count > 0)
                {
                    if (sent == dataReceived)
                    {
                        sent++;

                        server.Send(new TestClass()
                        {
                            Name = string.Join("", Enumerable.Range(0, 300).Select(a => index.ToString())),
                            Number = dataReceived
                        });
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }

                Assert.IsTrue(dataReceived >= packets);
            }
        }
    }
}
