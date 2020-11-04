// -----------------------------------------------------------------------
//   <copyright file="Activator.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Remote;

namespace Proto.Cluster.MongoIdentityLookup
{
    internal class MongoPlacementActor : IActor
    {
        private readonly Cluster _cluster;
        private readonly ILogger _logger;

        //pid -> the actor that we have created here
        //kind -> the actor kind
        //eventId -> the cluster wide eventId when this actor was created
        private readonly Dictionary<ClusterIdentity, PID> _myActors = new Dictionary<ClusterIdentity, PID>();

        private readonly IRemote _remote;
        private readonly MongoIdentityLookup _mongoIdentityLookup;

        public MongoPlacementActor(Cluster cluster, MongoIdentityLookup mongoIdentityLookup)
        {
            _cluster = cluster;
            _remote = _cluster.Remote;
            _mongoIdentityLookup = mongoIdentityLookup;
            _logger = Log.CreateLogger($"{nameof(MongoPlacementActor)}-{cluster.LoggerId}");
        }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
                   {
                       Started _             => Started(context),
                       ReceiveTimeout _      => ReceiveTimeout(context),
                       Terminated msg        => Terminated(context, msg),
                       ActivationRequest msg => ActivationRequest(context, msg),
                       _                     => Task.CompletedTask
                   };
        }

        private Task Started(IContext context)
        {
            context.SetReceiveTimeout(TimeSpan.FromSeconds(5));
            return Task.CompletedTask;
        }

        private Task ReceiveTimeout(IContext context)
        {
            context.SetReceiveTimeout(TimeSpan.FromSeconds(5));
            var count = _myActors.Count;
            _logger.LogInformation("Statistics: Actor Count {ActorCount}", count);
            return Task.CompletedTask;
        }

        private async Task Terminated(IContext context, Terminated msg)
        {
            //TODO: if this turns out to be perf intensive, lets look at optimizations for reverse lookups
            var (clusterIdentity, _) = _myActors.FirstOrDefault(kvp => kvp.Value.Equals(msg.Who));
            _myActors.Remove(clusterIdentity);
            await _mongoIdentityLookup.RemoveUniqueIdentityAsync(msg.Who.Id);
        }

        private Task ActivationRequest(IContext context, ActivationRequest msg)
        {
            var props = _cluster.GetClusterKind(msg.Kind);
            var identity = msg.Identity;
            var kind = msg.Kind;
            try
            {
                if (_myActors.TryGetValue(msg.ClusterIdentity, out var existing))
                {
                    //this identity already exists
                    var response = new ActivationResponse
                    {
                        Pid = existing
                    };
                    context.Respond(response);
                }
                else
                {
                    //this actor did not exist, lets spawn a new activation

                    //spawn and remember this actor
                    //as this id is unique for this activation (id+counter)
                    //we cannot get ProcessNameAlreadyExists exception here

                    var clusterProps = props.WithClusterInit(_cluster, msg.ClusterIdentity);
                    var pid = context.SpawnPrefix(clusterProps , identity);

                    _myActors[msg.ClusterIdentity] = pid;

                    var response = new ActivationResponse
                    {
                        Pid = pid
                    };
                    context.Respond(response);
                }
            }
            catch
            {
                var response = new ActivationResponse
                {
                    Pid = null
                };
                context.Respond(response);
            }

            return Task.CompletedTask;
        }
    }
}