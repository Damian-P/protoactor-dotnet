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
        private readonly RemoteActorSystem remote;

        public EndpointSupervisor(RemoteActorSystem remote) : base(remote)
        {
            this.remote = remote;
        }

        public override PID SpawnWriter(string address, ISpawnerContext context)
        {
            var writerProps =
                Props.FromProducer(
                        () => new EndpointWriter(remote, address, remote.RemoteConfig.ChannelOptions, remote.RemoteConfig.CallOptions, remote.RemoteConfig.ChannelCredentials)
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(remote, remote.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize));
            var writer = context.Spawn(writerProps);
            return writer;
        }
    }
}
