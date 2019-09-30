using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using BeardedManStudios;
using BeardedManStudios.Forge.Logging;
using BeardedManStudios.Forge.Managers;
using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.Forge.Networking.Generated;
using BeardedManStudios.Forge.Networking.Unity;
using BeardedManStudios.SimpleJSON;

namespace BeardedManStudios.Forge.Networking.Unity
{
    public class UnityNetworkBehaviourManager : INetworkBehaviorManager
    {
        public List<INetworkBehavior> FindUninitializedBehaviors()
        {
            List<INetworkBehavior> result = new List<INetworkBehavior>();
            foreach (NetworkBehavior behaviour in GameObject.FindObjectsOfType<NetworkBehavior>().Where(b => !b.Initialized)
                    .OrderBy(b => b.GetType().ToString())
                    .OrderBy(b => b.name)
                    .OrderBy(b => Vector3.Distance(Vector3.zero, b.transform.position)))
            {
                result.Add(behaviour);
            }
            return result;
        }

        public void Destroy(INetworkBehavior networkBehavior)
        {
            NetworkBehavior unityBehavior = networkBehavior as NetworkBehavior;
            GameObject.Destroy(unityBehavior.gameObject);
        }
    }

    /**
     * Provides Unity-specific interface to GameClient and GameServer.
     **/
    public partial class NetworkManager : MonoBehaviour
	{
		public static NetworkManager Instance { get; private set; }

        // Editable fields
		public ForgeSettings Settings;
        public bool AutomaticScenes = true;

        // Run-time accessors
        public GameServer Server { get; private set; }
        public GameClient Client { get; private set; }
        public MasterServerClient MasterServerClient { get; private set; }
        public bool IsServer { get { return Server != null; } }
        public NetWorker Networker { get { return Server != null ? Server.Networker : Client != null ? Client.Networker : null; } }

        // Internals
        private NetworkSettings _networkSettings;
        private UnityNetworkBehaviourManager _behaviourManager;
		private ObjectMapper objectMapper;

        public void StartClient(MasterServerResponse.Server serverDescription)
        {
            Client = new GameClient(_networkSettings, _behaviourManager, new NetworkObjectFactory(), MainThreadManager.Instance, serverDescription);
			Client.ResetSceneHandler += HandleSceneReset;
			Client.AddSceneHandler += HandleSceneAdd;
			Client.RemoveSceneHandler += HandleSceneRemove;
		}

		public void StartServer()
        {
            Server = new GameServer(_networkSettings, _behaviourManager, new NetworkObjectFactory());
        }

        public void StartMasterServerClient(MasterServerClient.MasterServerResponseDelegate responseHandler)
        {
            MasterServerClient = new MasterServerClient(_networkSettings, MainThreadManager.Instance, responseHandler);
        }

        public void Disconnect()
        {
            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }
            if (Server != null)
            {
                Server.Disconnect();
                Server = null;
            }
            if (MasterServerClient != null)
            {
                MasterServerClient.Networker.Disconnect(false);
                MasterServerClient = null;
            }
        }

        protected virtual void Awake()
		{
			if (Instance != null)
			{
				Destroy(gameObject);
				return;
			}

			Instance = this;

			// This object should move through scenes
			DontDestroyOnLoad(gameObject);

			if (Settings == null)
			{
				BMSLog.Log("No settings were provided. Trying to find default settings");
				Settings = FindObjectOfType<ForgeSettings>();
				if (Settings == null)
				{
					throw new BaseNetworkException("Could not find forge settings!");
				}
			}

            _behaviourManager = new UnityNetworkBehaviourManager();
			UnityObjectMapper.CreateInstance();

            // Convert Unity-specific ForgeSettings to base-level NetworkSettings
            _networkSettings = new NetworkSettings()
            {
                gameId = Settings.serverId,
                serverName = Settings.serverName,
                masterServerHost = Settings.masterServerHost,
                masterServerPort = Settings.masterServerPort,
            };
        }

        protected virtual void OnEnable()
		{
			if (AutomaticScenes)
			{
				SceneManager.sceneLoaded += SceneLoaded;
				SceneManager.sceneUnloaded += SceneUnloaded;
			}
		}

		protected virtual void OnDisable()
		{
			if (AutomaticScenes)
			{
				SceneManager.sceneLoaded -= SceneLoaded;
				SceneManager.sceneUnloaded -= SceneUnloaded;
			}
		}

		protected virtual void OnApplicationQuit()
		{
            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }
            if (Server != null)
            {
                Server.Disconnect();
                Server = null;
            }
        }

        protected virtual void Update()
		{
            if (Client != null)
            {
                Client.Update();
            }

            if (Server != null)
            {
                Server.Update();
            }
		}

		// Handle server request to reset current scene (load single)
		protected virtual void HandleSceneReset(int sceneId)
		{
			SceneManager.LoadSceneAsync(sceneId, LoadSceneMode.Single);
		}

		// Handle server request to load a scene additively
		protected virtual void HandleSceneAdd(int sceneId)
		{
			SceneManager.LoadSceneAsync(sceneId, LoadSceneMode.Additive);
		}

		// Handle server request to unload a scene
		protected virtual void HandleSceneRemove(int sceneId)
		{
			SceneManager.UnloadSceneAsync(sceneId);
		}

		protected virtual void SceneLoaded(Scene scene, LoadSceneMode mode)
		{
            if (mode == LoadSceneMode.Single)
            {
                if (Client != null)
                {
                    Client.OnSceneReset(scene.buildIndex);
                }
                if (Server != null)
                {
                    Server.OnSceneReset(scene.buildIndex);
                }
            }
            else if (mode == LoadSceneMode.Additive)
            {
                if (Client != null)
                {
                    Client.OnSceneAdded(scene.buildIndex);
                }
                if (Server != null)
                {
                    Server.OnSceneAdded(scene.buildIndex);
                }
            }
        }

		/// <summary>
		/// Callback for when a Scene has been unloaded
		/// </summary>
		/// <param name="scene"></param>
		protected virtual void SceneUnloaded(Scene scene)
		{
            if (Client != null)
            {
                Client.OnSceneRemoved(scene.buildIndex);
            }
            if (Server != null)
            {
                Server.OnSceneRemoved(scene.buildIndex);
            }
        }

        protected virtual void ProcessOthers(Transform obj, NetworkObject createTarget, ref uint idOffset, NetworkBehavior netBehavior = null)
        {
            int i;

            // Get the order of the components as they are in the inspector
            var components = obj.GetComponents<NetworkBehavior>();

            // Create each network object that is available
            for (i = 0; i < components.Length; i++)
            {
                if (components[i] == netBehavior)
                    continue;

                if (Server != null)
                {
                    var no = components[i].CreateNetworkObject(Server.Networker, 0);
                    FinalizeInitialization(obj.gameObject, components[i], no, obj.position, obj.rotation, false, true);
                }
                else
                {
                    var no = components[i].CreateNetworkObject(Client.Networker, 0);
                    components[i].AwaitNetworkBind(Client.Networker, createTarget, idOffset++);
                }
            }

            for (i = 0; i < obj.transform.childCount; i++)
                ProcessOthers(obj.transform.GetChild(i), createTarget, ref idOffset);
        }

        protected virtual void FinalizeInitialization(GameObject go, INetworkBehavior netBehavior, NetworkObject obj, Vector3? position = null, Quaternion? rotation = null, bool sendTransform = true, bool skipOthers = false)
        {
            if (Server != null)
                InitializedObject(netBehavior, obj);
            else
                obj.pendingInitialized += InitializedObject;

            if (position != null)
            {
                if (rotation != null)
                {
                    go.transform.position = position.Value;
                    go.transform.rotation = rotation.Value;
                }
                else
                    go.transform.position = position.Value;
            }

            if (!skipOthers)
            {
                // Go through all associated network behaviors in the hierarchy (including self) and
                // Assign their TempAttachCode for lookup later. Should use an incrementor or something
                uint idOffset = 1;
                ProcessOthers(go.transform, obj, ref idOffset, (NetworkBehavior)netBehavior);
            }

            go.SetActive(true);
        }
    }
}
