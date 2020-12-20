// -----------------------------------------------------------------------
// <copyright file="PartitionIdentityActor.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
// </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Timers;

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
        private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(10);

        //for how long do we wait after a topology change before we allow spawning new actors?
        //do note that this happens after a topology change which can be triggered by a timed out unhealthy service in the cluster provider
        //the time before the cluster becomes responsive again is TopologyChangeTimeout + Time for service to be unhealthy

        private static readonly TimeSpan TopologyChangeTimeout = TimeSpan.FromSeconds(3);

        private readonly Cluster _cluster;
        private readonly ILogger _logger;
        private readonly string _myAddress;

        private readonly Dictionary<ClusterIdentity, PID> _partitionLookup = new(); //actor/grain name to PID
        private readonly Dictionary<PID, ClusterIdentity> _reverseLookup = new();

        private readonly Rendezvous _rdv = new();

        private readonly Dictionary<ClusterIdentity, Task<ActivationResponse>> _spawns = new();

        private ulong _eventId;
        private DateTime _lastEventTimestamp;

        private ProcessingMode _mode = ProcessingMode.Waiting;
        private Task? _resumeProcessing;
        private CancellationTokenSource? _ct;

        public PartitionIdentityActor(Cluster cluster)
        {
            _logger = Log.CreateLogger($"{nameof(PartitionIdentityActor)}-{cluster.LoggerId}");
            _cluster = cluster;
            _myAddress = cluster.System.Address;
        }

        private TimeSpan StartWorkingIn => _lastEventTimestamp + TopologyChangeTimeout - DateTime.Now;

        public Task ReceiveAsync(IContext context) =>
            context.Message switch
            {
                Started _                => Start(context),
                ReceiveTimeout _         => ReceiveTimeout(context),
                ActivationRequest msg    => GetOrSpawn(msg, context),
                ActivationTerminated msg => ActivationTerminated(msg, context),
                ClusterTopology msg      => ClusterTopology(msg, context),
                Tick _                   => Tick(context),
                Stopping _               => OnStopping(),
                Terminated terminated    => Terminated(terminated, context),
                _                        => Unhandled()
            };

        private Task Terminated(Terminated terminated, IContext context)
        {
            // _logger.LogInformation("Terminated: {pid}", terminated.Who);
            if (_reverseLookup.TryGetValue(terminated.Who, out var identity))
            {
                _partitionLookup.Remove(identity);
                _reverseLookup.Remove(terminated.Who);
            }
            return Task.CompletedTask;
        }

        private static Task Unhandled() => Task.CompletedTask;

        private Task Start(IContext context)
        {
            _ct = context.Scheduler().SendRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), context.Self!, new Tick());
            _lastEventTimestamp = DateTime.Now;
            _logger.LogDebug("Started");
            PauseProcessing(context, StartWorkingIn);

            return Task.CompletedTask;
        }

        private Task OnStopping()
        {
            _ct?.Cancel();
            return Task.CompletedTask;
        }

        private Task Tick(IContext context)
        {
            var removedSpawnCount = 0;
            foreach (var spawn in _spawns.Where(x => x.Value.IsCompleted).ToArray())
            {
                removedSpawnCount++;
                _spawns.Remove(spawn.Key);
                if (_partitionLookup.Remove(spawn.Key, out var pid))
                    _reverseLookup.Remove(pid);
            }
            _logger.LogDebug(@"Statistics: Partition lookup Count {ActorCount}
Statistics: Partition reverseLookup Count {ActorCount}
Statistics: Spawns Count {spawnCount}
Statistics: Removed {removedSpawns} spawns", _partitionLookup.Count, _reverseLookup.Count, _spawns.Count, removedSpawnCount);
            return Task.CompletedTask;
        }

        private void PauseProcessing(IContext context, TimeSpan duration)
        {
            if (duration > TimeSpan.Zero)
            {
                _mode = ProcessingMode.Waiting;
                var resume = new TaskCompletionSource<bool>();
                _resumeProcessing = resume.Task;
                context.ReenterAfter(Task.Delay(duration), ConsiderResumeProcessing(context, resume));
            }
            else
                _mode = ProcessingMode.Working;
        }

        private Action ConsiderResumeProcessing(IContext context, TaskCompletionSource<bool> resume)
        {
            return () =>
            {
                var delay = StartWorkingIn;
                if (delay > TimeSpan.FromMilliseconds(1))
                {
                    _logger.LogDebug("Delaying activations with {Timespan}", delay);
                    context.ReenterAfter(Task.Delay(delay), ConsiderResumeProcessing(context, resume));
                }
                else
                {
                    _logger.LogDebug("Starting activations");
                    _mode = ProcessingMode.Working;
                    resume.SetResult(true);
                }
            };
        }

        private Task ReceiveTimeout(IContext context)
        {
            context.SetReceiveTimeout(IdleTimeout);
            _logger.LogInformation("I am idle");
            return Task.CompletedTask;
        }

        private async Task ClusterTopology(ClusterTopology msg, IContext context)
        {
            if (_eventId >= msg.EventId) return;

            _eventId = msg.EventId;
            _lastEventTimestamp = DateTime.Now;
            var members = msg.Members.ToArray();

            _rdv.UpdateMembers(members);

            //remove all identities we do no longer own.
            _partitionLookup.Clear();
            _reverseLookup.Clear();

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
                        TakeOwnership(actor, context);

                        if (!_partitionLookup.ContainsKey(actor.ClusterIdentity))
                            _logger.LogError("Ownership bug, we should own {Identity}", actor.ClusterIdentity);
                        else
                            _logger.LogDebug("I have ownership of {Identity}", actor.ClusterIdentity);
                    }
                }
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Failed to get identities");
            }


            //always do this when a member leaves, we need to redistribute the distributed-hash-table
            //no ifs or else, just always
            //ClearInvalidOwnership(context);

            var membersLookup = msg.Members.ToDictionary(m => m.Address, m => m);

            //scan through all id lookups and remove cases where the address is no longer part of cluster members
            foreach (var (actorId, pid) in _partitionLookup.ToArray())
            {
                if (!membersLookup.ContainsKey(pid.Address))
                {
                    _partitionLookup.Remove(actorId);
                    _reverseLookup.Remove(pid);
                }
            }

            if (_mode == ProcessingMode.Working) PauseProcessing(context, StartWorkingIn);
        }

        private Task ActivationTerminated(ActivationTerminated msg, IContext context)
        {
            var ownerAddress = _rdv.GetOwnerMemberByIdentity(msg.Identity);
            if (!string.IsNullOrWhiteSpace(ownerAddress) && ownerAddress != _myAddress)
            {
                var ownerPid = PartitionManager.RemotePartitionIdentityActor(ownerAddress);
                _logger.LogDebug("Tried to terminate activation on wrong node, forwarding");
                context.Forward(ownerPid);

                return Task.CompletedTask;
            }

            //TODO: handle correct incarnation/version
            _logger.LogDebug("Terminated {Pid}", msg.Pid);
            if (_partitionLookup.Remove(msg.ClusterIdentity, out var pid))
                _reverseLookup.Remove(pid);
            return Task.CompletedTask;
        }

        private void TakeOwnership(Activation msg, IContext context)
        {
            if (_partitionLookup.TryGetValue(msg.ClusterIdentity, out var pid))
            {
                //these are the same, that's good, just ignore message
                if (pid.Address == msg.Pid.Address) return;
            }

            // _logger.LogDebug("Taking Ownership of: {Identity}, pid: {Pid}", msg.Identity, msg.Pid);
            _partitionLookup[msg.ClusterIdentity] = msg.Pid;
            _reverseLookup[msg.Pid] = msg.ClusterIdentity;
            context.Watch(msg.Pid);
        }

        private Task GetOrSpawn(ActivationRequest msg, IContext context)
        {
            if (context.Sender is null)
            {
                _logger.LogCritical("NO SENDER IN GET OR SPAWN!!");
                return Task.CompletedTask;
            }


            var ownerAddress = _rdv.GetOwnerMemberByIdentity(msg.Identity);
            if (string.IsNullOrWhiteSpace(ownerAddress))
            {
                _logger.LogWarning("No members currently available for kind {Kind}", msg.Kind);
                context.Send(context.Self!, msg);
                return Task.CompletedTask;
            }
            if (ownerAddress != _myAddress)
            {
                var ownerPid = PartitionManager.RemotePartitionIdentityActor(ownerAddress);
                _logger.LogDebug("Tried to spawn on wrong node, forwarding");
                context.Forward(ownerPid);

                return Task.CompletedTask;
            }

            //Check if exist in current partition dictionary
            if (_partitionLookup.TryGetValue(msg.ClusterIdentity, out var pid))
            {
                context.Respond(new ActivationResponse { Pid = pid });
                return Task.CompletedTask;
            }

            // if (_mode == ProcessingMode.Waiting)
            // {
            //     if (_resumeProcessing is null)
            //         _logger.LogCritical("Reenter task was null in wait mode!");
            //     else
            //     {
            //         _logger.LogDebug("");

            //         context.ReenterAfter(_resumeProcessing, () => GetOrSpawn(msg, context));
            //         return Task.CompletedTask;
            //     }
            // }

            //Get activator
            var activatorAddress = _cluster.MemberList.GetActivator(msg.Kind, context.Sender.Address)?.Address;

            //just make the code analyzer understand the address is not null after this block
            if (activatorAddress is null || string.IsNullOrEmpty(activatorAddress))
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
            if (!_spawns.TryGetValue(msg.ClusterIdentity, out var res))
            {
                res = SpawnRemoteActor(msg, activatorAddress);
                _spawns.Add(msg.ClusterIdentity, res);
            }

            //execution ends here. context.ReenterAfter is invoked once the task completes
            //but still within the actors sequential execution
            //but other messages could have been processed in between

            //Await SpawningProcess
            context.ReenterAfter(
                res,
                rst =>
                {
                    var response = res.Result;
                    //TODO: as this is async, there might come in multiple ActivationRequests asking for this
                    //Identity, causing multiple activations


                    //Check if exist in current partition dictionary
                    //This is necessary to avoid race condition during partition map transfer.
                    if (_partitionLookup.TryGetValue(msg.ClusterIdentity, out pid))
                    {
                        context.Respond(new ActivationResponse {Pid = pid});
                        return Task.CompletedTask;
                    }

                    //Check if process is faulted
                    if (rst.IsFaulted)
                    {
                        _logger.LogCritical("Spawn faulted");
                        context.Respond(new DeadLetterResponse());
                        _spawns.Remove(msg.ClusterIdentity);
                        return Task.CompletedTask;
                    }

                    if (_partitionLookup.Remove(msg.ClusterIdentity, out var epid))
                        _reverseLookup.Remove(epid);

                    _partitionLookup[msg.ClusterIdentity] = response.Pid;
                    _reverseLookup[response.Pid] = msg.ClusterIdentity;

                    context.Respond(response);
                    context.Watch(response.Pid);
                    _spawns.Remove(msg.ClusterIdentity);

                    return Task.CompletedTask;
                }
            );
            return Task.CompletedTask;
        }

        private Task<ActivationResponse> SpawnRemoteActor(ActivationRequest req, string activator)
        {
            _logger.LogDebug("Spawning Remote Actor {Activator} {Identity} {Kind}", activator, req.Identity,
                req.Kind
            );
            return ActivateAsync(activator, req, _cluster.Config!.TimeoutTimespan);
        }

        //identical to Remote.SpawnNamedAsync, just using the special partition-activator for spawning
        private Task<ActivationResponse> ActivateAsync(string address, ActivationRequest req,
            TimeSpan timeout)
        {
            var activator = PartitionManager.RemotePartitionPlacementActor(address);

            return _cluster.System.Root.RequestAsync<ActivationResponse>(activator, req, timeout);

        }

        private enum ProcessingMode
        {
            Waiting,
            Working
        }
    }
}