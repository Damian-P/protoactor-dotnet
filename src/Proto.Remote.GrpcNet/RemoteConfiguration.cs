// -----------------------------------------------------------------------
//   <copyright file="RemoteConfiguration.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote
{
    public class RemoteConfiguration
    {
        public RemoteConfiguration(Serialization serialization, RemoteKindRegistry remoteKindRegistry, AspRemoteConfig remoteConfig)
        {
            Serialization = serialization;
            RemoteKindRegistry = remoteKindRegistry;
            RemoteConfig = remoteConfig;
        }

        public Serialization Serialization { get; set; }
        public RemoteKindRegistry RemoteKindRegistry { get; set; }
        public AspRemoteConfig RemoteConfig { get; set; }
    }
}