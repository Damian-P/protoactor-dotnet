// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote
{
    internal class RemoteProcess : Process
    {
        private readonly PID _pid;
        private readonly IInternalRemoteActorAsystem _remoteActorSystem;

        public RemoteProcess(IInternalRemoteActorAsystem remoteActorSystem, PID pid) : base(remoteActorSystem)
        {
            _remoteActorSystem = remoteActorSystem;
            _pid = pid;
        }

        protected override void SendUserMessage(PID _, object message) => Send(message);

        protected override void SendSystemMessage(PID _, object message) => Send(message);

        private void Send(object msg)
        {
            switch (msg)
            {
                case Watch w:
                {
                    var rw = new RemoteWatch(w.Watcher, _pid);
                    _remoteActorSystem.EndpointManager.RemoteWatch(rw);
                    break;
                }
                case Unwatch uw:
                {
                    var ruw = new RemoteUnwatch(uw.Watcher, _pid);
                    _remoteActorSystem.EndpointManager.RemoteUnwatch(ruw);
                    break;
                }
                default:
                    _remoteActorSystem.SendMessage(_pid, msg, -1);
                    break;
            }
        }
    }
}