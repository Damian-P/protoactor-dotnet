// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Proto.Remote;

namespace Proto.Cluster
{
    public static class Extensions
    {
        public static IServiceCollection AddClustering(
            this IServiceCollection services,
            string clusterName,
            IClusterProvider clusterProvider,
            Action<Cluster> configure = null)
        {
            services.AddHostedService<HostedClusteringService>();

            var actorSystem = services.AddSingleton<Cluster>(sp =>
            {
                var actorSystem = sp.GetRequiredService<ActorSystem>();
                var remoting = sp.GetRequiredService<IRemote>();
                var cluster = new Cluster(actorSystem, clusterName, clusterProvider);
                configure?.Invoke(cluster);
                return cluster;
            });
            return services;
        }

        public static Cluster AddClustering(this ActorSystem actorSystem, string clusterName, IClusterProvider clusterProvider, Action<Cluster> configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterName, clusterProvider);
            configure?.Invoke(cluster);
            return cluster;
        }
        public static Cluster AddClustering(this ActorSystem actorSystem, ClusterConfig clusterConfig, Action<Cluster> configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterConfig);
            configure?.Invoke(cluster);
            return cluster;
        }
        public static Task StartCluster(this ActorSystem actorSystem)
        {
            return actorSystem.Plugins.GetPlugin<Cluster>().Start();
        }
        public static Task StopCluster(this ActorSystem actorSystem, bool graceful = true)
        {
            return actorSystem.Plugins.GetPlugin<Cluster>().Shutdown(graceful);
        }
        public static Task<(PID, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind)
        {
            var cluster = actorSystem.Plugins.GetPlugin<Cluster>();
            return cluster.GetAsync(name, kind);
        }
        public static Task<(PID, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind, CancellationToken ct)
        {
            var cluster = actorSystem.Plugins.GetPlugin<Cluster>();
            return cluster.GetAsync(name, kind, ct);
        }
    }
}