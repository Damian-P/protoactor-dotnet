﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Proto.Remote.Tests.Messages;
using Xunit;

namespace Proto.Remote.Tests
{
    public class RemoteFixture: IAsyncLifetime
    {
        private ProtoService _service;
        public const string RemoteAddress = "localhost:12000";

        public IRemote Remote { get; private set; }
        public ActorSystem ActorSystem { get; private set; }

        public async Task InitializeAsync()
        {
            ActorSystem = new ActorSystem();

            var config =
                GrpcRemoteConfig.BindToLocalhost()
                    .WithEndpointWriterMaxRetries(2)
                    .WithEndpointWriterRetryBackOff(TimeSpan.FromMilliseconds(10))
                    .WithEndpointWriterRetryTimeSpan(TimeSpan.FromSeconds(120))
                    .WithProtoMessages(Messages.ProtosReflection.Descriptor);

            _service = new ProtoService(12000, "localhost");
            _service.StartAsync().Wait();

            Remote = new SelfHostedRemote(ActorSystem, config);
            await Remote.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await Remote.ShutdownAsync();
            await _service.StopAsync();
        }
    }
}