// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto.Remote;

namespace Proto.Cluster
{
    public class HostedClusteringService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly Cluster _cluster;

        public HostedClusteringService(
            ILogger<HostedClusteringService> logger,
            IHostApplicationLifetime appLifetime,
            Cluster cluster)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _cluster = cluster;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("StartAsync");
            _appLifetime.ApplicationStopping.Register(OnStopping);
            _appLifetime.ApplicationStopped.Register(OnStopped);
            _appLifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _logger.LogInformation("OnStarted has been called.");
            _cluster.Start().GetAwaiter().GetResult();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _cluster.Shutdown().GetAwaiter().GetResult();
        }

        private void OnStopped()
        {
            _logger.LogInformation("OnStopped has been called.");
        }
    }
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
        public static Cluster AddGrains<TGrains>(this Cluster cluster, TGrains grains)
        {
            cluster.System.Plugins.Add(typeof(TGrains), grains);
            return cluster;
        }
        public static ActorSystem AddClustering(this ActorSystem actorSystem, string clusterName, IClusterProvider clusterProvider, Action<Cluster> configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterName, clusterProvider);
            configure?.Invoke(cluster);
            return actorSystem;
        }
        public static ActorSystem AddClustering(this ActorSystem actorSystem, ClusterConfig clusterConfig, Action<Cluster> configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterConfig);
            configure?.Invoke(cluster);
            return actorSystem;
        }
        public static Task StartCluster(this ActorSystem actorSystem)
        {
            var cluster = actorSystem.Plugins[typeof(Cluster)] as Cluster;
            if (cluster == null) throw new InvalidOperationException("Clustering is not configured");
            return cluster.Start();
        }
        public static Task StopCluster(this ActorSystem actorSystem, bool graceful = true)
        {
            var cluster = actorSystem.Plugins[typeof(Cluster)] as Cluster;
            if (cluster == null) throw new InvalidOperationException("Clustering is not configured");
            return cluster.Shutdown(graceful);
        }
        public static Task<(PID, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind)
        {
            var cluster = actorSystem.Plugins[typeof(Cluster)] as Cluster;
            if (cluster == null) throw new InvalidOperationException("Clustering is not configured");
            return cluster.GetAsync(name, kind);
        }
        public static Task<(PID, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind, CancellationToken ct)
        {
            var cluster = actorSystem.Plugins[typeof(Cluster)] as Cluster;
            if (cluster == null) throw new InvalidOperationException("Clustering is not configured");
            return cluster.GetAsync(name, kind, ct);
        }
    }
}