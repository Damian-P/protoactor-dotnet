// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------
// Modified file in context of repo fork : https://github.com/Optis-World/protoactor-dotnet
// Copyright 2019 ANSYS, Inc.
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    [PublicAPI]
    public abstract class Remote<TRemoteConfig> : IRemote<TRemoteConfig>
    where TRemoteConfig : RemoteConfig, new()
    {
        protected static readonly ILogger Logger = Log.CreateLogger<IRemote>();
        protected readonly ActorSystem _system;
        protected readonly string _hostname;
        protected readonly int _port;
        public EndpointManager EndpointManager { get; }

        public bool IsStarted { get; private set; }

        public TRemoteConfig RemoteConfig { get; } = new TRemoteConfig();

        public RemoteKindRegistry RemoteKindRegistry { get; } = new RemoteKindRegistry();
        public PID ActivatorPid { get; private set; }

        public Serialization Serialization { get; } = new Serialization();

        RemoteConfig IRemoteConfiguration.RemoteConfig => RemoteConfig;

        public Remote(ActorSystem system, string hostname, int port, Action<IRemoteConfiguration<TRemoteConfig>>? configure = null)
        {
            _system = system;
            _system.Plugins.AddPlugin<IRemote>(this);
            configure?.Invoke(this);
            var channelProvider = GetChannelProvider();
            EndpointManager = new EndpointManager(this, system, channelProvider);
            system.ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(this, system, EndpointManager, pid));
            _hostname = hostname;
            _port = port;
        }

        protected abstract IChannelProvider GetChannelProvider();

        public virtual void Start()
        {
            if (IsStarted) return;
            IsStarted = true;
            EndpointManager.Start();
            SpawnActivator();
        }

        public virtual Task ShutdownAsync(bool graceful = true)
        {
            if (graceful)
            {
                EndpointManager.Stop();
                StopActivator();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Span a remote actor with auto-generated name
        /// </summary>
        /// <param name="address">Remote node address</param>
        /// <param name="kind">Actor kind, must be known on the remote node</param>
        /// <param name="timeout">Timeout for the confirmation to be received from the remote node</param>
        /// <returns></returns>
        public Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout) =>
            SpawnNamedAsync(address, "", kind, timeout);

        public async Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await _system.Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;
        }

        private PID ActivatorForAddress(string address) => new PID(address, "activator");


        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header!, message, pid, sender!, serializerId);
            EndpointManager.RemoteDeliver(env);
        }

        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(RemoteKindRegistry, _system))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            ActivatorPid = _system.Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => _system.Root.Stop(ActivatorPid);
    }
}