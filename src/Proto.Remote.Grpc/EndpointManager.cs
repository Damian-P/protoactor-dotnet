// -----------------------------------------------------------------------
//   <copyright file="RemoteActorSystem.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote.Grpc
{
    public class EndpointManager : Proto.Remote.EndpointManager
    {
        private readonly RemoteActorSystem remoteActorSystem;

        public override Proto.Remote.EndpointSupervisor GetEndpointSupervisor()
        {
            return new EndpointSupervisor(remoteActorSystem);
        }
        public EndpointManager(RemoteActorSystem remoteActorSystem) : base(remoteActorSystem)
        {
            this.remoteActorSystem = remoteActorSystem;
        }
    }
}