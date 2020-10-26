using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Cluster.Partition
{
    //This actor is responsible to keep track of identities owned by this member
    //it does not manage the cluster spawned actors itself, only identity->remote PID management
    //TLDR; this is a partition/bucket in the distributed hash table which makes up the identity lookup
    //
    //for spawning/activating cluster actors see PartitionActivator.cs
    internal class PartitionIdentityActor : IActor
    {
        //for how long do we wait before sending a ReceiveTimeout message?  (useful for liveliness checks on the actor, log it to show the actor is alive)
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

        //for how long do we wait when performing a identity handover?
        private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(3);

        //for how long do we wait after a topology change before we allow spawning new actors?
        //do note that this happens after a topology change which can be triggered by a timed out unhealthy service in the cluster provider
        //the time before the cluster becomes responsive again is TopologyChangeTimeout + Time for service to be unhealthy

        private static readonly TimeSpan TopologyChangeTimeout = TimeSpan.FromSeconds(3);

        private readonly Cluster _cluster;
        private readonly ILogger _logger;
        private readonly string _myAddress;

        private readonly Dictionary<string, (PID pid, string kind)> _partitionLookup =
            new Dictionary<string, (PID pid, string kind)>(); //actor/grain name to PID

        private readonly PartitionManager _partitionManager;
        private readonly Rendezvous _rdv = new Rendezvous();

        private readonly Dictionary<string, Task<ActivationResponse>> _spawns =
            new Dictionary<string, Task<ActivationResponse>>();

        private ulong _eventId;
        private DateTime _lastEventTimestamp;

        public PartitionIdentityActor(Cluster cluster, PartitionManager partitionManager)
        {
            _logger = Log.CreateLogger($"{nameof(PartitionIdentityActor)}-{cluster.LoggerId}");
            _cluster = cluster;
            _partitionManager = partitionManager;
            _myAddress = cluster.System.Address;
        }

        public Task ReceiveAsync(IContext context) =>
            context.Message switch
            {
                Started _                => Start(),
                ReceiveTimeout _         => ReceiveTimeout(context),
                ActivationRequest msg    => GetOrSpawn(msg, context),
                ActivationTerminated msg => ActivationTerminated(msg, context),
                ClusterTopology msg      => ClusterTopology(msg, context),
                _                        => Unhandled()
            };

        private static Task Unhandled() => Task.CompletedTask;

        private Task Start()
        {
            _lastEventTimestamp = DateTime.Now;
            _logger.LogDebug("Started");
            //       context.SetReceiveTimeout(TimeSpan.FromSeconds(5));
            return Task.CompletedTask;
        }

        private Task ReceiveTimeout(IContext context)
        {
            context.SetReceiveTimeout(IdleTimeout);
            _logger.LogInformation("I am idle");
            return Task.CompletedTask;
        }

        private async Task ClusterTopology(ClusterTopology msg, IContext context)
        {
            if (_eventId >= msg.EventId)
            {
                return;
            }

            _eventId = msg.EventId;
            _lastEventTimestamp = DateTime.Now;
            var members = msg.Members.ToArray();

            _rdv.UpdateMembers(members);

            //remove all identities we do no longer own.
            _partitionLookup.Clear();

            _logger.LogInformation("Topology change --- {EventId} --- pausing interactions for {Timeout}",
                _eventId, TopologyChangeTimeout
            );

            var requests = new List<Task<IdentityHandoverResponse>>();
            var requestMsg = new IdentityHandoverRequest
            {
                EventId = _eventId,
                Address = _myAddress
            };

            requestMsg.Members.AddRange(members);

            foreach (var member in members)
            {
                var activatorPid = PartitionManager.RemotePartitionPlacementActor(member.Address);
                var request =
                    context.RequestAsync<IdentityHandoverResponse>(activatorPid, requestMsg, HandoverTimeout);
                requests.Add(request);
            }

            try
            {
                _logger.LogDebug("Requesting ownerships");

                //built in timeout on each request above
                var responses = await Task.WhenAll(requests);
                _logger.LogDebug("Got ownerships {EventId}", _eventId);

                foreach (var response in responses)
                {
                    foreach (var actor in response.Actors)
                    {
                        TakeOwnership(actor);

                        if (!_partitionLookup.ContainsKey(actor.Identity))
                        {
                            _logger.LogError("Ownership bug, we should own {Identity}", actor.Identity);
                        }
                        else
                        {
                            _logger.LogDebug("I have ownership of {Identity}", actor.Identity);
                        }
                    }
                }
            }
            catch (Exception x)
            {
                _logger.LogError(x,"Failed to get identities");
            }


            //always do this when a member leaves, we need to redistribute the distributed-hash-table
            //no ifs or else, just always
            //ClearInvalidOwnership(context);

            var membersLookup = msg.Members.ToDictionary(m => m.Address, m => m);

            //scan through all id lookups and remove cases where the address is no longer part of cluster members
            foreach (var (actorId, (pid, _)) in _partitionLookup.ToArray())
            {
                if (!membersLookup.ContainsKey(pid.Address))
                {
                    _partitionLookup.Remove(actorId);
                }
            }
        }

        private Task ActivationTerminated(ActivationTerminated msg, IContext context)
        {
            var ownerAddress = _rdv.GetOwnerMemberByIdentity(msg.Identity);
            if (ownerAddress != _myAddress)
            {
                var ownerPid = PartitionManager.RemotePartitionIdentityActor(ownerAddress);
                _logger.LogWarning("Tried to terminate activation on wrong node, forwarding");
                context.Forward(ownerPid);

                return Task.CompletedTask;
            }

            //TODO: handle correct incarnation/version
            _logger.LogDebug("Terminated {Pid}", msg.Pid);
            _partitionLookup.Remove(msg.Identity);
            return Task.CompletedTask;
        }

        private void TakeOwnership(Activation msg)
        {
            if (_partitionLookup.TryGetValue(msg.Identity, out var existing))
            {
                //these are the same, that's good, just ignore message
                if (existing.pid.Address == msg.Pid.Address)
                {
                    return;
                }
            }

            _logger.LogDebug("Taking Ownership of: {Identity}, pid: {Pid}", msg.Identity, msg.Pid);
            _partitionLookup[msg.Identity] = (msg.Pid, msg.Kind);
        }


        private Task GetOrSpawn(ActivationRequest msg, IContext context)
        {
            
            if (context.Sender == null)
            {
                _logger.LogCritical("NO SENDER IN GET OR SPAWN!!");
            }
            
            PID sender = context.Sender!;

            var ownerAddress = _rdv.GetOwnerMemberByIdentity(msg.Identity);
            if (ownerAddress != _myAddress)
            {
                var ownerPid = PartitionManager.RemotePartitionIdentityActor(ownerAddress);
                _logger.LogWarning("Tried to spawn on wrong node, forwarding");
                context.Forward(ownerPid);

                return Task.CompletedTask;
            }

            //Check if exist in current partition dictionary
            if (_partitionLookup.TryGetValue(msg.Identity, out var info))
            {
                if (context.Sender == null)
                {
                    _logger.LogCritical("No sender 4");
                }
                context.Respond(new ActivationResponse {Pid = info.pid});
                return Task.CompletedTask;
            }

            if (SendLater(msg, context))
            {
                return Task.CompletedTask;
            }

            //Get activator
            var activatorAddress = _cluster.MemberList.GetActivator(msg.Kind, context.Sender!.Address)?.Address;

            //just make the code analyzer understand the address is not null after this block
            if (activatorAddress == null || string.IsNullOrEmpty(activatorAddress))
            {
                //No activator currently available, return unavailable
                _logger.LogWarning("No members currently available for kind {Kind}", msg.Kind);
                context.Respond(new ActivationResponse {Pid = null});
                return Task.CompletedTask;
            }

            //What is this?
            //in case the actor of msg.Name is not yet spawned. there could be multiple re-entrant
            //messages requesting it, we just reuse the same task for all those
            //once spawned, the key is removed from this dict
            if (!_spawns.TryGetValue(msg.Identity, out var res))
            {
                res = SpawnRemoteActor(msg, activatorAddress);
                _spawns.Add(msg.Identity, res);
            }

            //execution ends here. context.ReenterAfter is invoked once the task completes
            //but still within the actors sequential execution
            //but other messages could have been processed in between

            //Await SpawningProcess
            context.ReenterAfter( //TODO: reenter bug for sender?
                res,
                rst =>
                {
                    var response = res.Result;
                    //TODO: as this is async, there might come in multiple ActivationRequests asking for this
                    //Identity, causing multiple activations


                    //Check if exist in current partition dictionary
                    //This is necessary to avoid race condition during partition map transfer.
                    if (_partitionLookup.TryGetValue(msg.Identity, out info))
                    {
                        context.Send(sender,new ActivationResponse {Pid = info.pid});
                        return Task.CompletedTask;
                    }

                    //Check if process is faulted
                    if (rst.IsFaulted || response is null)
                    {
                        context.Send(sender, (object?)response ?? new VoidResponse());
                        _spawns.Remove(msg.Identity);
                        return Task.CompletedTask;
                    }


                    _partitionLookup[msg.Identity] = (response.Pid, msg.Kind);

                    context.Send(sender, response);

                    try
                    {
                        _spawns.Remove(msg.Identity);
                    }
                    catch(Exception x)
                    {
                        //debugging hack
                        Console.WriteLine(x);
                    }

                    return Task.CompletedTask;
                }
            );

            return Task.CompletedTask;
        }

        private bool SendLater(object msg, IContext context)
        {
            //TODO: buffer this in a queue and consume once we are past timestamp
            if (DateTime.Now > _lastEventTimestamp.Add(TopologyChangeTimeout))
            {
                return false;
            }

            var self = context.Self!;
            var sender = context.Sender;
            _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    _cluster.System.Root.Request(self, msg, sender);
                }
            );

            return true;
        }


        private async Task<ActivationResponse> SpawnRemoteActor(ActivationRequest req, string activator)
        {
            try
            {
                _logger.LogDebug("Spawning Remote Actor {Activator} {Identity} {Kind}", activator, req.Identity,
                    req.Kind
                );
                return await ActivateAsync(activator, req.Identity, req.Kind, _cluster.Config!.TimeoutTimespan);
            }
            catch
            {
                return null!;
            }
        }

        //identical to Remote.SpawnNamedAsync, just using the special partition-activator for spawning
        private async Task<ActivationResponse> ActivateAsync(string address, string identity, string kind,
            TimeSpan timeout)
        {
            var activator = PartitionManager.RemotePartitionPlacementActor(address);

            var eventId = _eventId;
            var res = await _cluster.System.Root.RequestAsync<ActivationResponse>(
                activator, new ActivationRequest
                {
                    Kind = kind,
                    Identity = identity
                }, timeout
            );

            return res;
        }
    }
}