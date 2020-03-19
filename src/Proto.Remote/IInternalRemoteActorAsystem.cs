// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote
{
    public interface IInternalRemoteActorAsystem : IRemoteActorSystem
    {
        EndpointManager EndpointManager { get; }
        IRemoteConfig RemoteConfig { get; }
        void SendMessage(PID pid, object msg, int serializerId);
    }
}