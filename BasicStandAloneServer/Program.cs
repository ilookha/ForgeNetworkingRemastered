using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.Forge.Networking.Generated;
using BeardedManStudios.Forge.Managers;
using System;
using System.Collections.Generic;

namespace BasicStandAloneServer
{
	class EmptyNetworkBehaviorManager : INetworkBehaviorManager
	{
		public List<INetworkBehavior> FindUninitializedBehaviors()
		{
			return new List<INetworkBehavior>();
		}
		public void Destroy(INetworkBehavior networkBehaviour)
		{

		}
	}

	class Program
    {
        private static List<BasicCubeNetworkObject> cubes = new List<BasicCubeNetworkObject>();

		private static void Main(string[] args)
        {
			NetworkSettings settings = new NetworkSettings();
			settings.masterServerHost = "127.0.0.1";
			GameServer gameServer = new GameServer(settings, new EmptyNetworkBehaviorManager(), new NetworkObjectFactory());
            NetworkObject.Factory = new NetworkObjectFactory();

            while (true)
            {
				gameServer.Update();

                if (Console.ReadLine().ToLower() == "exit")
                {
                    break;
                }

				System.Threading.Thread.Sleep(0);
            }
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
