// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static ActorSystem AddRemoteOverGrpc(this ActorSystem actorSystem, string hostname, int port,
            Action<IRemoteConfiguration> configure = null)
        {
            var remote = new SelfHostedRemoteServerOverGrpc(actorSystem, hostname, port, configure);
            return actorSystem;
        }
    }
}