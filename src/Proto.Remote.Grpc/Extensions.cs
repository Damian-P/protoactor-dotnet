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
        public static ActorSystem AddRemotingOverGrpc(this ActorSystem actorSystem, string hostanme, int port, Action<RemotingConfiguration> configure = null)
        {
            var remote = new SelfHostedRemoteServerOverGrpc(actorSystem, hostanme, port, configure);
            return actorSystem;
        }
    }
}