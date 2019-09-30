using System;
using System.Collections.Generic;
using BeardedManStudios.Forge.Networking;

namespace BeardedManStudios.Forge.Managers
{
    public interface INetworkBehaviorManager
    {
        List<INetworkBehavior> FindUninitializedBehaviors();
        void Destroy(INetworkBehavior networkBehaviour);
    }
}
