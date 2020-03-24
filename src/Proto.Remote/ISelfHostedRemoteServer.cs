// -----------------------------------------------------------------------
//   <copyright file="ISelfHostedRemoteServer.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public interface IRemote
    {
        bool IsStarted { get; }
        RemotingConfiguration RemotingConfiguration { get; }
        Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout);
        Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout);
        Task Start();
        Task Stop(bool graceful = true);
        void SendMessage(PID pid, object msg, int serializerId);
    }
}