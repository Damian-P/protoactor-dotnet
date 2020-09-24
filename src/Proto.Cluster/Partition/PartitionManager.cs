// -----------------------------------------------------------------------
//   <copyright file="Partition.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using Proto.Mailbox;

namespace Proto.Cluster.Partition
{
    //helper to interact with partition actors on this and other members
    internal class PartitionManager
    {
        private const string PartitionIdentityActorName = "partition-identity";
        private const string PartitionPlacementActorName = "partition-activator";
        private readonly Cluster _cluster;
        private readonly IRootContext _context;
        private readonly ActorSystem _system;
        private PID _partitionActivator = null!;
        private PID _partitionActor = null!;
        private readonly bool _isClient;


        internal PartitionManager(Cluster cluster, bool isClient)
        {
            _cluster = cluster;
            _system = cluster.System;
            _context = _system.Root;
            _isClient = isClient;
        }

        internal PartitionMemberSelector Selector { get; } = new PartitionMemberSelector();

        private ushort PriorityDecider(object message)
        {
            return message switch
            {
                ClusterTopology _ => 5,
                IdentityHandoverRequest _ => 4,
                ActivationTerminated _ => 3,
                ActivationRequest _ => 2,
                _ => 0
            };
        }

        public void Setup()
        {

            if (_isClient)
            {
                var eventId = 0ul;
                //make sure selector is updated first
                _system.EventStream.Subscribe<ClusterTopology>(e =>
                    {
                        if (e.EventId > eventId)
                        {
                            eventId = e.EventId;
                            Selector.Update(e.Members.ToArray());
                        }
                    }
                );
            }
            else
            {
                var partitionActorProps = Props
                    .FromProducer(() => new PartitionIdentityActor(_cluster, this))
                    .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy)
                    .WithMailbox(() => PrioritizedUnboundedMailbox.Create(5, PriorityDecider));

                _partitionActor = _context.SpawnNamed(partitionActorProps, PartitionIdentityActorName);

                var partitionActivatorProps =
                    Props.FromProducer(() => new PartitionPlacementActor(_cluster, this))
                    .WithMailbox(() => PrioritizedUnboundedMailbox.Create(5, PriorityDecider));
                    
                _partitionActivator = _context.SpawnNamed(partitionActivatorProps, PartitionPlacementActorName);

                //synchronous subscribe to keep accurate

                var eventId = 0ul;
                //make sure selector is updated first
                _system.EventStream.Subscribe<ClusterTopology>(e =>
                    {
                        if (e.EventId > eventId)
                        {
                            eventId = e.EventId;
                            _cluster.MemberList.BroadcastEvent(e);

                            Selector.Update(e.Members.ToArray());
                            _context.Send(_partitionActor, e);
                            _context.Send(_partitionActivator, e);
                        }
                    }
                );
            }
        }

        public void Shutdown()
        {
            if (_isClient)
            {

            }
            else
            {
                _context.StopAsync(_partitionActivator).GetAwaiter().GetResult();
                _context.StopAsync(_partitionActor).GetAwaiter().GetResult();
            }
        }

        public PID RemotePartitionIdentityActor(string address) => new PID(address, PartitionIdentityActorName);

        public PID RemotePartitionPlacementActor(string address) => new PID(address, PartitionPlacementActorName);
    }
}