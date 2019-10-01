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
using BeardedManStudios.Source.Threading;

namespace BeardedManStudios.Forge.Managers
{
	public class GameClient
	{
		public delegate BMSByte DataWriteHandler();
		public DataWriteHandler DataWriteDelegate_ViewInitialize;
		
		public delegate void MasterServerResponseHandler(MasterServerResponse masterServerResponse);
		public MasterServerResponseHandler MasterServerResponseDelegate;

        public delegate void SceneManagementDelegate(int sceneId);
        public SceneManagementDelegate ResetSceneHandler;
        public SceneManagementDelegate AddSceneHandler;
        public SceneManagementDelegate RemoveSceneHandler;

        public NetWorker Networker { get; protected set; }
		public Dictionary<int, INetworkBehavior> pendingObjects = new Dictionary<int, INetworkBehavior>();
		public Dictionary<int, NetworkObject> pendingNetworkObjects = new Dictionary<int, NetworkObject>();

		public NetworkSettings Settings;
		public MasterServerResponse.Server ServerDescription;

        /// <summary>
        /// Provides engine-agnostic way of accessing network objects
        /// </summary>
        protected INetworkBehaviorManager _behaviorManager;
		
        /// <summary>
        /// Allows running game-side callbacks on the main thread
        /// </summary>
		protected IThreadRunner _mainThreadRunner;

        protected List<int> loadedScenes = new List<int>();
        protected List<int> loadingScenes = new List<int>();
		
		public GameClient(NetworkSettings settings, INetworkBehaviorManager behaviorManager, INetworkObjectFactory networkObjectFactory, IThreadRunner mainThreadRunner, MasterServerResponse.Server serverDescription)
		{
			Settings = settings ?? throw new BaseNetworkException("Could not find forge settings!");

			NetworkObject.Factory = networkObjectFactory ?? throw new BaseNetworkException("Network object factory must be provided");
            _behaviorManager = behaviorManager ?? throw new BaseNetworkException("Behavior manager must be provided");
			_mainThreadRunner = mainThreadRunner ?? throw new BaseNetworkException("Main thread runner must be provided");
			
			ConnectToGameServer(serverDescription);
		}

		protected virtual void CreatePendingObjects(NetworkObject obj)
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
				Networker.objectCreated -= CreatePendingObjects;
		}

		protected virtual void NetworkerDisconnected(NetWorker sender)
		{
			Networker.disconnected -= NetworkerDisconnected;
		}
		
		public void ConnectToGameServer(MasterServerResponse.Server serverDescription)
		{
			ServerDescription = serverDescription;
			
			if (serverDescription.Protocol == "tcp")
			{
				Networker = new TCPClient();
				((TCPClient)Networker).Connect(serverDescription.Address, serverDescription.Port);
			}
			else if (serverDescription.Protocol == "udp")
			{
				Networker = new UDPClient();
				((UDPClient)Networker).Connect(serverDescription.Address, serverDescription.Port, Settings.natServerHost, Settings.natServerPort);
			}
			#if !UNITY_IOS && !UNITY_ANDROID
			else if (serverDescription.Protocol == "udp")
			{
				Networker = new TCPClientWebsockets();
				((TCPClientWebsockets)Networker).Connect(serverDescription.Address, serverDescription.Port);
			}
			#endif
			if (Networker == null)
				throw new BaseNetworkException("No socket of type " + serverDescription.Protocol + " could be established");
			
			if (!Networker.IsBound)
			{
				throw new BaseNetworkException("NetWorker failed to bind");
			}

            Networker.objectCreated += CreatePendingObjects;
            Networker.binaryMessageReceived += ReadBinary;
        }

        public virtual void Disconnect()
		{
			Networker.objectCreated -= CreatePendingObjects;

			if (Networker != null)
				Networker.Disconnect(false);

			NetWorker.EndSession();

			NetworkObject.ClearNetworkObjects(Networker);
			pendingObjects.Clear();
			pendingNetworkObjects.Clear();
			Networker = null;
		}

		public void Update()
		{
			if (Networker != null)
			{
				for (int i = 0; i < Networker.NetworkObjectList.Count; i++)
					Networker.NetworkObjectList[i].InterpolateUpdate();
			}
		}

		protected virtual void ReadBinary(NetworkingPlayer player, Binary frame, NetWorker sender)
		{
			if (frame.GroupId == MessageGroupIds.VIEW_INITIALIZE)
			{
                BMSLog.LogFormat("GameClient: VIEW_INITIALIZE");

				int sceneId = frame.StreamData.GetBasicType<int>();
				GameServer.ViewUpdateMode loadMode = (GameServer.ViewUpdateMode)frame.StreamData.GetBasicType<int>();

				lock (NetworkObject.PendingCreatesLock)
                {
                    // We need to halt the creation of network objects until we load the scene
                    Networker.PendCreates = true;

                    loadingScenes.Clear();
                    loadingScenes.Add(sceneId);
                }

                if (ResetSceneHandler != null)
                {
                    _mainThreadRunner.Execute(() =>
                    {
                        ResetSceneHandler(sceneId);
                    });
                }

                return;
			}

			if (frame.GroupId == MessageGroupIds.VIEW_CHANGE)
            {
                BMSLog.LogFormat("GameClient: VIEW_CHANGE");

                int sceneId = frame.StreamData.GetBasicType<int>();
                GameServer.ViewUpdateMode updateMode = (GameServer.ViewUpdateMode)frame.StreamData.GetBasicType<int>();

                if (updateMode == GameServer.ViewUpdateMode.Add)
                {
                    lock (NetworkObject.PendingCreatesLock)
                    {
                        // We need to halt the creation of network objects until we load the scene
                        Networker.PendCreates = true;
                        loadingScenes.Add(sceneId);
                    }

                    if (AddSceneHandler != null)
                    {
                        _mainThreadRunner.Execute(() =>
                        {
                            AddSceneHandler(sceneId);
                        });
                    }
                }
                else if (updateMode == GameServer.ViewUpdateMode.Remove)
                {
                    loadingScenes.Remove(sceneId);

                    UnloadSceneNetworkObjects(sceneId);

                    if (RemoveSceneHandler != null)
                    {
                        _mainThreadRunner.Execute(() =>
                        {
                            RemoveSceneHandler(sceneId);
                        });
                    }
                }
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
			if (networker is IServer)
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
			else
			{
				if (networker is TCPClientBase)
					((TCPClientBase)networker).Send(frame);
#if STEAMWORKS
				else if (networker is SteamP2PClient)
					((SteamP2PClient)networker).Send(frame, true);
#endif
				else
					((UDPClient)networker).Send(frame, true);
			}
		}

        public virtual void OnSceneReset(int sceneId)
        {
            if (Networker == null)
                return;

            // If we are loading a completely new scene then we will need
            // to clear out all the old objects that were stored as they
            // are no longer needed
            pendingObjects.Clear();
            pendingNetworkObjects.Clear();
            loadedScenes.Clear();

            lock (NetworkObject.PendingCreatesLock)
            {
                loadingScenes.Remove(sceneId);
            }
            loadedScenes.Add(sceneId);

            // Notify the server
            BMSByte data = ObjectMapper.BMSByte(sceneId, (int)GameServer.ViewUpdateMode.Reset);
            Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.Server, MessageGroupIds.VIEW_CHANGE, Networker is BaseTCP);

            SendFrame(Networker, frame);

            InitializeBehaviours(sceneId, GameServer.ViewUpdateMode.Reset);
        }

        public virtual void OnSceneAdded(int sceneId)
        {
			if (Networker == null)
				return;

			lock (NetworkObject.PendingCreatesLock)
            {
                loadingScenes.Remove(sceneId);
            }
            loadedScenes.Add(sceneId);

            // Notify the server
            BMSByte data = ObjectMapper.BMSByte(sceneId, (int)GameServer.ViewUpdateMode.Add);
            Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.Server, MessageGroupIds.VIEW_CHANGE, Networker is BaseTCP);

            SendFrame(Networker, frame);

            InitializeBehaviours(sceneId, GameServer.ViewUpdateMode.Add);
        }

        /// <summary>
        /// Callback for when a Scene has been unloaded
        /// </summary>
        /// <param name="scene"></param>
        public virtual void OnSceneRemoved(int sceneId)
        {
			if (Networker == null)
				return;

			loadedScenes.Remove(sceneId);

            // Notify the server
            BMSByte data = ObjectMapper.BMSByte(sceneId, (int)GameServer.ViewUpdateMode.Remove);
            Binary frame = new Binary(Networker.Time.Timestep, false, data, Receivers.Server, MessageGroupIds.VIEW_CHANGE, Networker is BaseTCP);

            SendFrame(Networker, frame);
        }

        private void InitializeBehaviours(int sceneId, GameServer.ViewUpdateMode updateMode)
        {
			// Go through all of the current NetworkBehaviors in the order that Unity finds them in
			// and associate them with the id that the network will be giving them as a lookup
			int currentAttachCode = 1;
            var behaviors = _behaviorManager.FindUninitializedBehaviors();

            if (behaviors.Count == 0)
			{
                if (loadingScenes.Count > 0)
                    NetworkObject.Flush(Networker, loadingScenes, CreatePendingObjects);
                else
                {
                    NetworkObject.Flush(Networker, loadingScenes);
                    if(pendingObjects.Count == 0)
                        Networker.objectCreated -= CreatePendingObjects;
                }

				return;
			}

			foreach (INetworkBehavior behavior in behaviors)
			{
				behavior.TempAttachCode = sceneId << 16;
				behavior.TempAttachCode += currentAttachCode++;
				behavior.TempAttachCode = -behavior.TempAttachCode;
			}

            // This would occur if objects in the additive scene arrives at the same time as the
            // "single" scene and were flushed.
            if (updateMode == GameServer.ViewUpdateMode.Add && pendingNetworkObjects.Count > 0)
            {
                NetworkObject foundNetworkObject;
                for (int i = 0; i < behaviors.Count; i++)
                {
                    if (pendingNetworkObjects.TryGetValue(behaviors[i].TempAttachCode, out foundNetworkObject))
                    {
                        behaviors[i].Initialize(foundNetworkObject);
                        pendingNetworkObjects.Remove(behaviors[i].TempAttachCode);
                        behaviors.RemoveAt(i--);
                    }
                }
            }

            foreach (INetworkBehavior behavior in behaviors)
                pendingObjects.Add(behavior.TempAttachCode, behavior);

            NetworkObject.Flush(Networker, loadingScenes, CreatePendingObjects);

            if (pendingObjects.Count == 0 && loadingScenes.Count == 0)
                Networker.objectCreated -= CreatePendingObjects;
			else if (pendingObjects.Count != 0 && loadingScenes.Count == 0)
            {
	            // Pending network behavior list is not empty when there are no more scenes to load.
	            // Probably network behaviours that were placed in the scene have already been destroyed on the server and other clients!
	            foreach (var pair in pendingObjects)
	            {
		            _behaviorManager.Destroy(pair.Value);
	            }
                pendingObjects.Clear();
            }
		}

		/// <summary>
		/// A helper function to retrieve a NetworkBehavior by its network id.
		/// </summary>
		/// <param name="id">Network id of the gameobject</param>
        public INetworkBehavior GetBehaviorByNetworkId(uint id)
        {
            if (Networker == null ) //Only check Networker, as NetworkObjects are always initiliased.
            {
                //Debug.LogWarning("Networker is null. Network manager has not been initiliased.");
                return null;
            }

            NetworkObject foundNetworkObject = null;
			if (!Networker.NetworkObjects.TryGetValue(id, out foundNetworkObject) || foundNetworkObject.AttachedBehavior == null)
            {
                //Debug.LogWarning("No object found by id or object has no attached behavior.");
                return null;
            }

            return foundNetworkObject.AttachedBehavior;
        }

        /// <summary>
        /// Called when you want to remove all network objects from the Networker list for a scene
        /// </summary>
        /// <param name="sceneId"></param>
        void UnloadSceneNetworkObjects(int sceneId)
		{
			if (sceneId >= 0)
			{
				List<NetworkObject> networkObjectsToDestroy = new List<NetworkObject>();

				// Gets all networkObjects related to the scene we are destorying
				Networker.IterateNetworkObjects(networkObject =>
				{
					INetworkBehavior networkBehavior = networkObject.AttachedBehavior;
					if (networkBehavior != null)
					{
						if (networkBehavior.SceneId == sceneId)
						{
							networkObjectsToDestroy.Add(networkObject);
						}
					}
				});

				Networker.ManualRemove(networkObjectsToDestroy);

				foreach (NetworkObject networkObject in networkObjectsToDestroy)
				{
					pendingNetworkObjects.Remove(networkObject.CreateCode);
				}
			}
		}
	}
}
