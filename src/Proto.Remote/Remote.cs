// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public abstract class Remote : IRemote
    {
        protected static readonly ILogger Logger = Log.CreateLogger(typeof(RemotingConfiguration).FullName);
        protected readonly RemotingConfiguration _remote;
        public RemotingConfiguration RemotingConfiguration { get { return _remote; } }

        public bool IsStarted { get; private set; }

        protected readonly ActorSystem _system;
        protected readonly string _hostname;
        protected readonly int _port;

        protected readonly EndpointManager _endpointManager;

        public Remote(ActorSystem system, IChannelProvider channelProvider, string hostname, int port, Action<RemotingConfiguration> configure = null)
        {
            _system = system;
            _system.Plugins.Add(typeof(IRemote), this);
            _remote = new RemotingConfiguration();
            configure?.Invoke(_remote);
            _endpointManager = new EndpointManager(system, _remote.RemoteConfig, _remote.Serialization, channelProvider);
            system.ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(system, _endpointManager, pid));
            _hostname = hostname;
            _port = port;
        }

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
        private PID _activatorPid;
        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(_remote.RemoteKindRegistry, _system))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            _activatorPid = _system.Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => _system.Root.Stop(_activatorPid);

        private PID ActivatorForAddress(string address) => new PID(address, "activator");

        public virtual Task Start()
        {
            if (IsStarted) return Task.CompletedTask;
            IsStarted = true;
            _endpointManager.Start();
            SpawnActivator();
            return Task.CompletedTask;
        }

        public virtual async Task Stop(bool graceful = true)
        {
            if (graceful)
            {
                await _endpointManager.StopAsync();
                StopActivator();
            }
        }
        public void SendMessage(PID pid, object msg, int serializerId)
            => _endpointManager.SendMessage(pid, msg, serializerId);
    }
}