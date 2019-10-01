using System;
using System.Collections.Generic;
using System.Linq;
using BeardedManStudios;
using BeardedManStudios.Forge.Logging;
using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.Forge.Networking.Generated;
using BeardedManStudios.Forge.Networking.SQP;
using BeardedManStudios.SimpleJSON;

namespace BeardedManStudios.Forge.Managers
{
	public class GameServer
	{
		public delegate BMSByte DataWriteHandler();
		public DataWriteHandler DataWriteDelegate_ViewInitialize;

		public NetWorker Networker { get; protected set; }
		public NetWorker MasterServerNetworker { get; protected set; }
		public Dictionary<int, INetworkBehavior> pendingObjects = new Dictionary<int, INetworkBehavior>();
		public Dictionary<int, NetworkObject> pendingNetworkObjects = new Dictionary<int, NetworkObject>();

		public NetworkSettings Settings;

        public enum ViewUpdateMode
        {
            Reset = 0,
            Add = 1,
            Remove = 2,
        }

		/// <summary>
		/// Internal flag to indicate that the Initialize method has been called.
		/// </summary>
		protected bool initialized;

		/// <summary>
		/// The service that handles Server Query Protocol requests
		/// </summary>
		protected SQPServer sqpServer;

        /// <summary>
        /// Provides engine-agnostic way of accessing network objects
        /// </summary>
        protected INetworkBehaviorManager _behaviorManager;

        protected List<int> loadedScenes = new List<int>();
        protected List<int> loadingScenes = new List<int>();

#if FN_WEBSERVER
		MVCWebServer.ForgeWebServer webserver = null;
#endif

        public GameServer(NetworkSettings settings, INetworkBehaviorManager behaviorManager, INetworkObjectFactory networkObjectFactory)
		{
			Settings = settings;
			_behaviorManager = behaviorManager;
			NetworkObject.Factory = networkObjectFactory;

			if (Settings == null)
			{
				throw new BaseNetworkException("Could not find forge settings!");
			}
            if (_behaviorManager == null)
            {
                throw new BaseNetworkException("Behavior manager must be provided");
            }

            Initialize();
		}

		private void Initialize()
		{
            BMSLog.LogFormat("GameServer: Initialize");

            if (Settings.useTCP)
			{
				Networker = new TCPServer(64);
			}
			else
			{
				Networker = new UDPServer(64);
			}

			Networker.objectCreated += Networker_objectCreated;
            Networker.playerTimeout += Networker_playerTimeout;
			Networker.binaryMessageReceived += ReadBinary;

            if (Settings.useTCP)
            {
                (Networker as TCPServer).Connect(string.Empty, Settings.gameServerPort);
            }
            else
            {
                if (Settings.natServerHost.Trim().Length == 0)
                {
                    (Networker as UDPServer).Connect(host:string.Empty, port:Settings.gameServerPort, enableTimeouts:Settings.enableTimeouts);
                }
                else
                {
                    (Networker as UDPServer).Connect(port: Settings.gameServerPort, natHost: Settings.natServerHost, natPort: Settings.natServerPort, enableTimeouts: Settings.enableTimeouts);
                }
            }

            if (!Networker.IsBound)
            {
                throw new Exception("NetWorker failed to bind");
            }

			if (Settings.enableSQP)
			{
				sqpServer = new SQPServer(Settings.SQPPort);
			}

			if (!string.IsNullOrEmpty(Settings.masterServerHost))
			{
				RegisterOnMasterServer(GenerateMasterServerRegisterData(Networker));
			}

			Networker.playerAccepted += PlayerAcceptedSceneSetup;

#if FN_WEBSERVER
			string pathToFiles = "fnwww/html";
			Dictionary<string, string> pages = new Dictionary<string, string>();
			TextAsset[] assets = Resources.LoadAll<TextAsset>(pathToFiles);
			foreach (TextAsset a in assets)
				pages.Add(a.name, a.text);

			webserver = new MVCWebServer.ForgeWebServer(networker, pages);
			webserver.Start();
#endif
			initialized = true;
		}

        protected virtual void Networker_objectCreated(NetworkObject obj)
		{
			INetworkBehavior behavior;

			if (!pendingObjects.TryGetValue(obj.CreateCode, out behavior))
			{
				if (obj.CreateCode < 0)
					pendingNetworkObjects.Add(obj.CreateCode, obj);

				return;
			}

			behavior.Initialize(obj);
			pendingObjects.Remove(obj.CreateCode);

			if (pendingObjects.Count == 0 && loadingScenes.Count == 0)
				Networker.objectCreated -= Networker_objectCreated;
		}

        private void Networker_playerTimeout(NetworkingPlayer player, NetWorker sender)
        {
            BMSLog.LogFormat("GameServer: player {0} timed out", player.ToString());
        }

        private JSONNode GenerateMasterServerRegisterData(NetWorker gameServer)
		{
			// Create the get request with the desired filters
			JSONNode sendData = JSONNode.Parse("{}");
			JSONClass registerData = new JSONClass();
			registerData.Add("id", Settings.gameId);
			registerData.Add("name", Settings.serverName);
			registerData.Add("port", new JSONData(Settings.gameServerPort));
			registerData.Add("playerCount", new JSONData(gameServer.Players.Count));
			registerData.Add("maxPlayers", new JSONData(gameServer.MaxConnections));
			registerData.Add("comment", Settings.serverComment);
			registerData.Add("type", Settings.gameType);
			registerData.Add("mode", Settings.gameMode);
			registerData.Add("protocol", gameServer is UDPServer ? "udp" : "tcp");
			registerData.Add("elo", new JSONData(Settings.eloRequired));
			registerData.Add("useElo", new JSONData(Settings.useElo));
			sendData.Add("register", registerData);

			return sendData;
		}

		protected virtual void RegisterOnMasterServer(JSONNode masterServerData)
		{
			// The Master Server communicates over TCP
			TCPMasterClient client = new TCPMasterClient();

			// Once this client has been accepted by the master server it should send it's get request
			client.serverAccepted += (sender) =>
			{
				try
				{
                    BMSLog.LogFormat("GameServer: Connected to MasterServer");
                    Text temp = Text.CreateFromString(client.Time.Timestep, masterServerData.ToString(), true, Receivers.Server, MessageGroupIds.MASTER_SERVER_REGISTER, true);

					//Debug.Log(temp.GetData().Length);
					// Send the request to the server
					client.Send(temp);

					Networker.disconnected += s =>
					{
                        BMSLog.LogFormat("GameServer: Disconnected from MasterServer");

                        client.Disconnect(false);
						MasterServerNetworker = null;
					};
				}
				catch
				{
					// If anything fails, then this client needs to be disconnected
					client.Disconnect(true);
					client = null;
				}
			};

			client.Connect(Settings.masterServerHost, Settings.masterServerPort);

			Networker.disconnected += NetworkerDisconnected;
			MasterServerNetworker = client;
		}

		protected virtual void NetworkerDisconnected(NetWorker sender)
		{
			Networker.disconnected -= NetworkerDisconnected;
			MasterServerNetworker.Disconnect(false);
			MasterServerNetworker = null;
		}

		public virtual void UpdateMasterServerListing(NetWorker server, string comment = null, string gameType = null, string mode = null)
		{
			JSONNode sendData = JSONNode.Parse("{}");
			JSONClass registerData = new JSONClass();

			registerData.Add("playerCount", new JSONData(server.Players.Count));
			if (comment != null) registerData.Add("comment", comment);
			if (gameType != null) registerData.Add("type", gameType);
			if (mode != null) registerData.Add("mode", mode);
			registerData.Add("port", new JSONData(server.Port));

			sendData.Add("update", registerData);

			UpdateMasterServerListing(sendData);
		}

		protected virtual void UpdateMasterServerListing(JSONNode masterServerData)
		{
			if (string.IsNullOrEmpty(Settings.masterServerHost))
			{
				throw new System.Exception("This server is not registered on a master server, please ensure that you are passing a master server host and port into the initialize");
			}

			if (MasterServerNetworker == null)
			{
				throw new System.Exception("Connection to master server is closed. Make sure to be connected to master server before attempting to update");
			}

			// The Master Server communicates over TCP
			TCPMasterClient client = new TCPMasterClient();

			// Once this client has been accepted by the master server it should send it's update request
			client.serverAccepted += (sender) =>
			{
				try
				{
					Text temp = Text.CreateFromString(client.Time.Timestep, masterServerData.ToString(), true, Receivers.Server, MessageGroupIds.MASTER_SERVER_UPDATE, true);

					// Send the request to the server
					client.Send(temp);
				}
				finally
				{
					// If anything fails, then this client needs to be disconnected
					client.Disconnect(true);
					client = null;
				}
			};

			client.Connect(Settings.masterServerHost, Settings.masterServerPort);
		}

		public virtual void Disconnect()
		{
#if FN_WEBSERVER
			webserver.Stop();
#endif

			Networker.objectCreated -= Networker_objectCreated;

			if (Networker != null)
				Networker.Disconnect(false);

			if (sqpServer != null)
				sqpServer.ShutDown();

			if (MasterServerNetworker != null)
				MasterServerNetworker.Disconnect(false);

			NetWorker.EndSession();

			NetworkObject.ClearNetworkObjects(Networker);
			pendingObjects.Clear();
			pendingNetworkObjects.Clear();
			MasterServerNetworker = null;
			Networker = null;
		}

		public void Update()
		{
			if (Networker != null)
			{
				for (int i = 0; i < Networker.NetworkObjectList.Count; i++)
					Networker.NetworkObjectList[i].InterpolateUpdate();
			}

			if (sqpServer != null)
			{
				UpdateSQPServer();
			}
		}

		protected virtual void UpdateSQPServer()
		{
			// Update SQP data with current values
			var sid = sqpServer.ServerInfoData;

			sid.Port = Networker.Port;
			// This count will include the host, for dedicated server setups it needs to be count-1
			sid.CurrentPlayers = Convert.ToUInt16(Networker.Players.Count);
			sid.MaxPlayers = Convert.ToUInt16(Networker.MaxConnections);
			sid.ServerName = Settings.serverName;
			sid.ServerType = Settings.gameType;

			sqpServer.Update();
		}

		/// <summary>
		/// Called automatically when a new player is accepted and sends the player
		/// the currently loaded scene indexes for the client to load
		/// </summary>
		/// <param name="player">The player that was just accepted</param>
		/// <param name="sender">The sending <see cref="NetWorker"/></param>
		protected virtual void PlayerAcceptedSceneSetup(NetworkingPlayer player, NetWorker sender)
		{
            BMSLog.LogFormat("GameServer: PlayerAcceptedSceneSetup {0}", player.ToString());

			// Go through all the loaded scene indexes and send them to the connecting player
			for (int i = 0; i < loadedScenes.Count; i++)
			{
				// Consider the first loaded scene to be non-additive
				if (i == 0)
				{
					BMSByte data = ObjectMapper.BMSByte(loadedScenes[i], (int)ViewUpdateMode.Reset);
					Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.Target, MessageGroupIds.VIEW_INITIALIZE, Networker is BaseTCP);
					SendFrame(sender, frame, player);
				}
				else
				{
					BMSByte data = ObjectMapper.BMSByte(loadedScenes[i], (int)ViewUpdateMode.Add);
					Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.Target, MessageGroupIds.VIEW_CHANGE, Networker is BaseTCP);
					SendFrame(sender, frame, player);
				}
			}
		}

		protected virtual void ReadBinary(NetworkingPlayer player, Binary frame, NetWorker sender)
		{
			if (frame.GroupId == MessageGroupIds.VIEW_CHANGE)
			{
				// The client has loaded the scene
				return;
			}
		}

		/// <summary>
		/// A wrapper around the various raw send methods for the client and server types
		/// </summary>
		/// <param name="networker">The networker that is going to be sending the data</param>
		/// <param name="frame">The frame that is to be sent across the network</param>
		/// <param name="targetPlayer">The player to send the frame to, if null then will send to all</param>
		public static void SendFrame(NetWorker networker, FrameStream frame, NetworkingPlayer targetPlayer = null)
		{
			if (targetPlayer != null)
			{
				if (networker is TCPServer)
					((TCPServer)networker).SendToPlayer(frame, targetPlayer);
#if STEAMWORKS
				else if (networker is SteamP2PServer)
					((SteamP2PServer)networker).Send(targetPlayer, frame, true);
#endif
				else
					((UDPServer)networker).Send(targetPlayer, frame, true);
			}
			else
			{
				if (networker is TCPServer)
					((TCPServer)networker).SendAll(frame);
#if STEAMWORKS
				else if (networker is SteamP2PServer)
					((SteamP2PServer)networker).Send(frame, true);
#endif
				else
					((UDPServer)networker).Send(frame, true);
			}
		}

        public void OnSceneReset(int sceneId)
        {
            // The NetworkManager has not yet been initialized with a Networker.
            if (!initialized)
                return;

            BMSLog.LogFormat("GameServer: OnSceneReset({0})", sceneId);

            // If we are loading a completely new scene then we will need
            // to clear out all the old objects that were stored as they
            // are no longer needed
            pendingObjects.Clear();
            pendingNetworkObjects.Clear();
            loadedScenes.Clear();

			loadedScenes.Add(sceneId);

			BMSByte data = ObjectMapper.BMSByte(sceneId, (int)ViewUpdateMode.Reset);
            Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.All, MessageGroupIds.VIEW_INITIALIZE, Networker is BaseTCP);

            SendFrame(Networker, frame);

            InitializeBehaviours(sceneId);
        }

        public void OnSceneAdded(int sceneId)
        {
            // The NetworkManager has not yet been initialized with a Networker.
            if (!initialized)
                return;

            BMSLog.LogFormat("GameServer: OnSceneAdded({0})", sceneId);

            lock (NetworkObject.PendingCreatesLock)
            {
                loadingScenes.Remove(sceneId);
            }
			loadedScenes.Add(sceneId);

			BMSByte data = ObjectMapper.BMSByte(sceneId, (int)ViewUpdateMode.Add);
			Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.All, MessageGroupIds.VIEW_CHANGE, Networker is BaseTCP);

			SendFrame(Networker, frame);

            InitializeBehaviours(sceneId);
        }

        /// <summary>
        /// Callback for when a Scene has been unloaded
        /// </summary>
        /// <param name="sceneId"></param>
        public void OnSceneRemoved(int sceneId)
		{
			// The NetworkManager has not yet been initialized with a Networker.
			if (!initialized)
				return;

            BMSLog.LogFormat("GameServer: OnSceneRemoved({0})", sceneId);

            loadedScenes.Remove(sceneId);

            BMSByte data = ObjectMapper.BMSByte(sceneId, (int)ViewUpdateMode.Remove);
            Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.All, MessageGroupIds.VIEW_CHANGE, false);

			// Send the binary frame to the clients
			SendFrame(Networker, frame);
		}

        private void InitializeBehaviours(int sceneId)
        {
            // Go through all of the current NetworkBehaviors in the order that Unity finds them in
            // and associate them with the id that the network will be giving them as a lookup
            int currentAttachCode = 1;
            var behaviors = _behaviorManager.FindUninitializedBehaviors();

            if (behaviors.Count == 0)
            {
                return;
            }

            foreach (INetworkBehavior behavior in behaviors)
            {
                behavior.TempAttachCode = sceneId << 16;
                behavior.TempAttachCode += currentAttachCode++;
                behavior.TempAttachCode = -behavior.TempAttachCode;
            }

            // Go through all of the pending NetworkBehavior objects and initialize them on the network
            foreach (INetworkBehavior behavior in behaviors)
                behavior.Initialize(Networker);
        }
    }
}
