using System.Collections.Generic;
using BeardedManStudios.Forge.Networking;
using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.SimpleJSON;
using BeardedManStudios.Source.Threading;

namespace BeardedManStudios.Forge.Managers
{
	public class MasterServerClient
	{
		public delegate void MasterServerResponseDelegate(MasterServerResponse masterServerResponse);
        public MasterServerResponseDelegate MasterServerResponseHandler;

        public NetworkSettings Settings;

        public TCPMasterClient Networker { get; private set; }

        /// <summary>
        /// Allows running game-side callbacks on the main thread
        /// </summary>
		protected IThreadRunner m_MainThreadRunner;

        protected List<int> loadedScenes = new List<int>();
        protected List<int> loadingScenes = new List<int>();
		
		public MasterServerClient(NetworkSettings settings, IThreadRunner mainThreadRunner, MasterServerResponseDelegate responseHandler)
		{
			Settings = settings;
			if (Settings == null)
			{
				throw new BaseNetworkException("Could not find forge settings!");
			}
			
			m_MainThreadRunner = mainThreadRunner;
            if (m_MainThreadRunner == null)
            {
                throw new BaseNetworkException("Main thread runner must be provided");
            }

            MasterServerResponseHandler = responseHandler;

            FetchServerList();
		}

		public void FetchServerList()
		{
			// The Master Server communicates over TCP
			Networker = new TCPMasterClient();

            // Once this client has been accepted by the master server it should send it's get request
            Networker.serverAccepted += (sender) =>
			{
				try
				{
					// Create the get request with the desired filters
					JSONNode sendData = JSONNode.Parse("{}");
					JSONClass getData = new JSONClass();
					getData.Add("id", Settings.gameId);
					getData.Add("type", Settings.gameType);
					getData.Add("mode", Settings.gameMode);
					getData.Add("elo", new JSONData(Settings.myElo));

					sendData.Add("get", getData);

                    // Send the request to the server
                    Networker.Send(Text.CreateFromString(Networker.Time.Timestep, sendData.ToString(), true, Receivers.Server, MessageGroupIds.MASTER_SERVER_GET, true));
				}
				catch
				{
                    // If anything fails, then this client needs to be disconnected
                    Networker.Disconnect(true);
                    Networker = null;

                    if (MasterServerResponseHandler != null)
                    {
                        m_MainThreadRunner.Execute(() =>
                        {
                            MasterServerResponseHandler(null);
                        });
                    }
                }
            };

            // An event that is raised when the server responds with hosts
            Networker.textMessageReceived += (player, frame, sender) =>
			{
				try
				{
					// Get the list of hosts to iterate through from the frame payload
					JSONNode data = JSONNode.Parse(frame.ToString());
					if (data["hosts"] != null)
					{
						MasterServerResponse response = new MasterServerResponse(data["hosts"].AsArray);
						if (MasterServerResponseHandler != null)
						{
							// Forward response to the game and expect ConnectToGameServer() to be called later
							m_MainThreadRunner.Execute(() =>
							{
                                MasterServerResponseHandler(response);
							});
						}
					}
					else
					{
						if (MasterServerResponseHandler != null)
						{
							m_MainThreadRunner.Execute(() =>
							{
                                MasterServerResponseHandler(null);
							});
						}
					}
				}
				finally
				{
					if (Networker != null)
					{
                        // If we succeed or fail the client needs to disconnect from the Master Server
                        Networker.Disconnect(true);
                        Networker = null;
					}
				}
			};

			try
			{
                Networker.Connect(Settings.masterServerHost, Settings.masterServerPort);
			}
			catch (System.Exception ex)
			{
				if (MasterServerResponseHandler != null)
				{
					m_MainThreadRunner.Execute(() =>
					{
                        MasterServerResponseHandler(null);
					});
				}
			}
		}
	}
}
