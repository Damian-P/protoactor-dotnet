// -----------------------------------------------------------------------
//   <copyright file="EndpointSupervisor.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.Grpc
{
    public class EndpointSupervisor : Proto.Remote.EndpointSupervisor
    {
        private readonly RemoteActorSystem _remoteActorSystem;

        public EndpointSupervisor(RemoteActorSystem remote) : base(remote)
        {
            _remoteActorSystem = remote;
        }

        public override PID SpawnWriter(string address, ISpawnerContext context)
        {
            var writerProps =
                Props.FromProducer(
                        () => new EndpointWriter(_remoteActorSystem, address, _remoteActorSystem.RemoteConfig.ChannelOptions, _remoteActorSystem.RemoteConfig.CallOptions, _remoteActorSystem.RemoteConfig.ChannelCredentials)
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(_remoteActorSystem, _remoteActorSystem.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize));
            var writer = context.Spawn(writerProps);
            return writer;
        }
    }
}
