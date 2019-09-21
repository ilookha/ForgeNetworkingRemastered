using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.Forge.Networking.Generated;
using BeardedManStudios.SimpleJSON;
using System;
using System.Collections.Generic;

namespace BasicStandAloneServer
{
    class Program
    {
        private static List<BasicCubeNetworkObject> cubes = new List<BasicCubeNetworkObject>();

		private static void RegisterOnMasterServer(NetWorker server, string masterServerHost, ushort masterServerPort)
		{
			// The Master Server communicates over TCP
			TCPMasterClient client = new TCPMasterClient();

			// Once this client has been accepted by the master server it should send its registration request
			client.serverAccepted += (sender) =>
			{
				try
				{
					Console.WriteLine("\nAccepted on the Master Server");

					// Feed server information into the registration request
					JSONNode sendData = JSONNode.Parse("{}");
					JSONClass registerData = new JSONClass();
					registerData.Add("id", "myGame");
					registerData.Add("name", "BasicStandAloneServer");
					registerData.Add("port", new JSONData(server.Port));
					registerData.Add("playerCount", new JSONData(server.Players.Count));
					registerData.Add("maxPlayers", new JSONData(server.MaxConnections));
					registerData.Add("comment", "Demo comment...");
					registerData.Add("type", "Deathmatch");
					registerData.Add("mode", "Teams");
					registerData.Add("protocol", server is BaseUDP ? "udp" : "tcp");

					sendData.Add("register", registerData);

					// Send the registration request to the server
					Text textFrame = Text.CreateFromString(client.Time.Timestep, sendData.ToString(), true, Receivers.Server, MessageGroupIds.MASTER_SERVER_REGISTER, true);
					client.Send(textFrame);

					server.disconnected += s =>
					{
						client.Disconnect(false);
					};
					client.playerDisconnected += (player, timeoutSender) =>
					{
						// Server is the only networking player. Losing it means the connection is dead.
						Console.WriteLine("\nDisconnected from the Master Server");
						client.Disconnect(false);
					};
				}
				catch
				{
					// If anything fails, then this client needs to be disconnected
					Console.WriteLine("\nFailed connecting to the Master Server");
					client.Disconnect(true);
					client = null;
				}
			};

			client.Connect(masterServerHost, masterServerPort);
		}

		private static void Main(string[] args)
        {
            NetworkObject.Factory = new NetworkObjectFactory();

            int playerCount = 32;
            UDPServer networkHandle = new UDPServer(playerCount);
            networkHandle.textMessageReceived += ReadTextFrame;
            networkHandle.playerAccepted += PlayerAccepted;

            networkHandle.objectCreated += NetworkObjectCreated;

            networkHandle.Connect();

			RegisterOnMasterServer(networkHandle, string.Empty, 15940);

            while (true)
            {
                if (Console.ReadLine().ToLower() == "exit")
                {
                    break;
                }
            }

            networkHandle.Disconnect(false);
        }

        private static void NetworkObjectCreated(NetworkObject target)
        {
            if (target is BasicCubeNetworkObject)
            {
                var cube = (BasicCubeNetworkObject)target;
                cubes.Add(cube);
                target.Networker.FlushCreateActions(target);
                target.ReleaseCreateBuffer();
            }
        }

        private static void PlayerAccepted(NetworkingPlayer player, NetWorker sender)
        {
            Console.WriteLine($"New player accepted with id {player.NetworkId}");
        }

        private static void ReadTextFrame(NetworkingPlayer player, BeardedManStudios.Forge.Networking.Frame.Text frame, NetWorker sender)
        {
            Console.WriteLine("Read: " + frame.ToString());
        }
    }
}
