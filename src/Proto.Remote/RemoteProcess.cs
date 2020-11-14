// -----------------------------------------------------------------------
//   <copyright file="RemoteProcess.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

namespace Proto.Remote
{
    public class RemoteProcess : Process
    {
        private readonly PID _pid;
        private readonly PID? _endpoint;

        public RemoteProcess(ActorSystem system, EndpointManager endpointManager, PID pid) : base(system)
        {
            _pid = pid;
            _endpoint = endpointManager.GetEndpoint(pid.Address);
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
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);
            var env = new RemoteDeliver(header!, message, _pid, sender!, -1);
            if (_endpoint is not null)
                System.Root.Send(_endpoint, env);
            else
                System.EventStream.Publish(new DeadLetterEvent(_pid, message, sender));
        }

        private void Unwatch(Unwatch uw)
        {
            var ruw = new RemoteUnwatch(uw.Watcher, _pid);
            if (_endpoint is not null)
                System.Root.Send(_endpoint, ruw);
            else
                System.EventStream.Publish(new DeadLetterEvent(_pid, ruw, null));
        }

        private void Watch(Watch w)
        {
            var rw = new RemoteWatch(w.Watcher, _pid);
            if (_endpoint is not null)
                System.Root.Send(_endpoint, rw);
            else
                System.EventStream.Publish(new DeadLetterEvent(_pid, rw, null));
        }
    }
}