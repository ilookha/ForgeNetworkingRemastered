using System;
using BeardedManStudios.Forge.Networking;

namespace BeardedManStudios.Forge.Managers
{
	public class NetworkSettings
	{
		// Forge behaviour settings
		public bool useMainThreadManagerForRPCs = true;
		public bool useTCP = false;

		public bool connectUsingMatchmaking = false;
		public bool useElo = false;
		public bool useInlineChat = false;
		public int myElo = 0;
		public int eloRequired = 0;

        public ushort gameServerPort = NetWorker.DEFAULT_PORT;
		public string masterServerHost = string.Empty;
		public ushort masterServerPort = 15940;
		public string natServerHost = string.Empty;
		public ushort natServerPort = 15941;

		// Server Query Protocol settings
		public bool getLocalNetworkConnections = false;
		public bool enableSQP = true;
		public ushort SQPPort = 15900;

		// Game Settings
		public string gameId = "myGame";
		public string serverName = "Forge Game";
		public string gameType = "Deathmatch";
		public string gameMode = "Teams";
		public string serverComment = "Demo comment...";
	}
}
