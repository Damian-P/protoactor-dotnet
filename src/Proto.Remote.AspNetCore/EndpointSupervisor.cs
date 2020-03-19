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

namespace Proto.Remote.AspNetCore
{
    public class EndpointSupervisor : Proto.Remote.EndpointSupervisor
    {
        private readonly Remote.RemoteActorSystemBase _remoteActorSystem;

        public EndpointSupervisor(Remote.RemoteActorSystemBase remote) : base(remote)
        {
            _remoteActorSystem = remote;
        }

        public override PID SpawnWriter(string address, ISpawnerContext context)
        {
            var writerProps =
                Props.FromProducer(
                        () => new EndpointWriter(_remoteActorSystem, address)
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(_remoteActorSystem, _remoteActorSystem.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize));
            var writer = context.Spawn(writerProps);
            return writer;
        }
    }
}
