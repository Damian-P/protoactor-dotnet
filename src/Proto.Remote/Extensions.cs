// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static void StartRemote(this ActorSystem actorSystem)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            remote.Start();
        }

        public static void StopRemote(this ActorSystem actorSystem, bool graceful = true)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            remote.Stop(graceful);
        }

        public static Task<ActorPidResponse> SpawnAsync(this ActorSystem actorSystem, string address, string kind, TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnAsync(address, kind, timeout);
        }
        public static Task<ActorPidResponse> SpawnNamedAsync(this ActorSystem actorSystem, string address, string name, string kind, TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnNamedAsync(address, name, kind, timeout);
        }

    }
}