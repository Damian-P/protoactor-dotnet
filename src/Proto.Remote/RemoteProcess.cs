// -----------------------------------------------------------------------
//   <copyright file="RemoteProcess.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote
{
    public class RemoteProcess : Process
    {
        private readonly EndpointManager _endpointManager;
        private readonly PID _pid;

        public RemoteProcess(ActorSystem system, EndpointManager endpointManager, PID pid) : base(system)
        {
            _endpointManager = endpointManager;
            _pid = pid;
        }

        protected override void SendUserMessage(PID _, object message) => Send(message);

        protected override void SendSystemMessage(PID _, object message) => Send(message);

        private void Send(object msg)
        {
            switch (msg)
            {
                case Watch w:
                    Watch(w);
                    break;
                case Unwatch uw:
                    Unwatch(uw);
                    break;
                default:
                    SendMessage(msg);
                    break;
            }
        }

        private void SendMessage(object msg)
        {
            var endpoint = _endpointManager.GetEndpoint(_pid.Address);
            if (endpoint is null)
            {
                (var message, var sender, var _) = Proto.MessageEnvelope.Unwrap(msg);
                System.EventStream.Publish(new DeadLetterEvent(_pid, message, sender));
                if (sender is not null)
                    System.Root.Send(sender, msg is PoisonPill
                    ? new Terminated { Who = _pid, Why = TerminatedReason.AddressTerminated }
                    : new DeadLetterResponse { Target = _pid }
                );
                return;
            }
            endpoint.SendMessage(_pid, msg, -1);
        }

        private void Unwatch(Unwatch uw)
        {
            var ruw = new RemoteUnwatch(uw.Watcher, _pid);
            _endpointManager.GetEndpoint(_pid.Address)?.RemoteUnwatch(ruw);
        }

        private void Watch(Watch w)
        {
            var endpoint = _endpointManager.GetEndpoint(_pid.Address);
            if (endpoint is null)
            {
                w.Watcher.SendSystemMessage(System, new Terminated { Who = _pid, Why = TerminatedReason.AddressTerminated });
                return;
            }
            var rw = new RemoteWatch(w.Watcher, _pid);
            endpoint.RemoteWatch(rw);
        }
    }
}