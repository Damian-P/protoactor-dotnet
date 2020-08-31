// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static IRemote AddRemote(this ActorSystem actorSystem, string hostname, int port,
            Action<IRemoteConfiguration>? configure = null, Action<List<ChannelOption>>? configureChannelOptions = null)
        {
            var remote = new SelfHostedRemote(actorSystem, hostname, port, configure, configureChannelOptions);
            return remote;
        }
        public static void StartRemote(this ActorSystem actorSystem)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            remote.Start();
        }

        public static Task ShutdownRemoteAsync(this ActorSystem actorSystem, bool graceful = true)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.ShutdownAsync(graceful);
        }

        public static Task<ActorPidResponse> SpawnAsync(this ActorSystem actorSystem, string address, string kind,
            TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnAsync(address, kind, timeout);
        }

        public static Task<ActorPidResponse> SpawnNamedAsync(this ActorSystem actorSystem, string address, string name,
            string kind, TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnNamedAsync(address, name, kind, timeout);
        }
    }
}