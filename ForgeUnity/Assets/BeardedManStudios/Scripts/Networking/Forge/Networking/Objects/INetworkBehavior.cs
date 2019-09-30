﻿namespace BeardedManStudios.Forge.Networking
{
	public interface INetworkBehavior
	{
		void Initialize(NetworkObject obj);
		void Initialize(NetWorker networker, byte[] metadata = null);
        int TempAttachCode { get; set; }
        int SceneId { get; }
    }
}
