// -----------------------------------------------------------------------
//   <copyright file="IRemote.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public interface IRemote : IProtoPlugin, IRemoteConfiguration
    {
        bool IsStarted { get; }
        Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout);
        Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout);
        void Start();
        Task Stop(bool graceful = true);
        void SendMessage(PID pid, object msg, int serializerId);
    }
    public interface IRemoteConfiguration
    {
        RemoteConfig RemoteConfig { get; }
        RemoteKindRegistry RemoteKindRegistry { get; }
        Serialization Serialization { get; }
    }
}