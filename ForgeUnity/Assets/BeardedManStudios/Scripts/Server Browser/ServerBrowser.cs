using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BeardedManStudios.Forge.Managers;
using BeardedManStudios.SimpleJSON;

namespace BeardedManStudios.Forge.Networking.Unity
{
	public class ServerBrowser : MonoBehaviour
	{
		public string masterServerHost = "127.0.0.1";
		public ushort masterServerPort = 15940;
		public string natServerHost = "";
		public ushort natServerPort = Nat.NatHolePunch.DEFAULT_NAT_SERVER_PORT;

		public string gameId = "myGame";
		public string gameType = "any";
		public string gameMode = "all";

		public Transform content = null;
		public GameObject serverOption = null;
		public GameObject networkManager = null;
		MasterServerClient client = null;

		private void Awake()
		{
			MainThreadManager.Create();
		}

		private void Start()
		{
			Refresh();
            if (networkManager == null)
            {
                Debug.LogWarning("A network manager was not provided, generating a new one instead");
                GameObject obj = new GameObject("Network Manager");
                obj.AddComponent<NetworkManager>();
            }
            else
                Instantiate(networkManager);
        }

        public void CreateServerOption(string name, UnityEngine.Events.UnityAction callback)
		{
			MainThreadManager.Run(() =>
			{
				var option = Instantiate(serverOption);
				option.transform.SetParent(content);
				var browserItem = option.GetComponent<ServerBrowserItem>();
				if (browserItem != null)
					browserItem.SetData(name, callback);
			});
		}

        public void Refresh()
        {
            // Clear out all the currently listed servers
            for (int i = content.childCount - 1; i >= 0; --i)
                Destroy(content.GetChild(i).gameObject);

            // The Master Server communicates over TCP
            NetworkSettings networkSettings = new NetworkSettings()
            {
                masterServerHost = masterServerHost,
                masterServerPort = masterServerPort,
                natServerHost = natServerHost,
                natServerPort = natServerPort,
                gameId = gameId,
                gameType = gameType,
                gameMode = gameMode,
            };

            client = new MasterServerClient(networkSettings, MainThreadManager.Instance, (response) => {
                if (response != null && response.serverResponse.Count > 0)
                {
                    // Go through all of the available hosts and add them to the server browser
                    foreach (MasterServerResponse.Server serverDescription in response.serverResponse)
                    {
                        // name, address, port, comment, type, mode, players, maxPlayers, protocol
                        CreateServerOption(name, () =>
                        {
                            NetworkManager.Instance.StartClient(serverDescription);
                            Connected();
                        });
                    }
                }
            });
		}

		public void Connected()
		{
			if (!NetworkManager.Instance.Networker.IsBound)
			{
				Debug.LogError("NetWorker failed to bind");
				return;
			}

			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
		}
	}
}
