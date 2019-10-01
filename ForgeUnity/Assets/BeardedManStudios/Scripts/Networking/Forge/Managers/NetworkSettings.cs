using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Nat;

namespace BeardedManStudios.Forge.Managers
{
	public class NetworkSettings
	{
		// Forge features
		public bool useMainThreadManagerForRPCs = true;
		public bool useTCP = false;

		public bool connectUsingMatchmaking = false;
		public bool useElo = false;
		public bool useInlineChat = false;
		public int myElo = 0;
		public int eloRequired = 0;

		// Addresses
        public ushort gameServerPort = NetWorker.DEFAULT_PORT;
		public string masterServerHost = string.Empty;
		public ushort masterServerPort = 15940;
		public string natServerHost = string.Empty;
		public ushort natServerPort = NatHolePunch.DEFAULT_NAT_SERVER_PORT;

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

		// Debug settings
		public bool enableTimeouts = true;
	}
}
