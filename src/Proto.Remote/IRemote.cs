// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Proto.Remote
{
    public interface IRemote: Extensions.IActorSystemExtension<IRemote>
    {
        RemoteConfig Config { get; }
        ActorSystem System { get; }
        // Only used in tests ?
        void SendMessage(PID pid, object msg, int serializerId);
        Task ShutdownAsync(bool graceful = true);
        Task StartAsync();
    }
}