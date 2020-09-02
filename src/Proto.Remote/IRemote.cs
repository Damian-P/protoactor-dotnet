// -----------------------------------------------------------------------
//   <copyright file="IRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
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
        Task ShutdownAsync(bool graceful = true);
        void SendMessage(PID pid, object msg, int serializerId);
    }
    public interface IRemote<TRemoteConfig> : IRemote, IRemoteConfiguration<TRemoteConfig>
    where TRemoteConfig : RemoteConfig, new()
    {

    }
}