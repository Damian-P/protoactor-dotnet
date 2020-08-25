// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
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
    public class ClusterHostedService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly Cluster _cluster;

        public ClusterHostedService(
            ILogger<ClusterHostedService> logger,
            IHostApplicationLifetime appLifetime,
            Cluster cluster)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _cluster = cluster;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);
            _appLifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _cluster.StartAsync().GetAwaiter().GetResult();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _cluster.ShutdownAsync().GetAwaiter().GetResult();
        }
    }
    public static class Extensions
    {
        public static IServiceCollection AddClustering(
            this IServiceCollection services,
            string clusterName,
            IClusterProvider clusterProvider,
            Action<Cluster>? configure = null)
        {
            services.AddHostedService<ClusterHostedService>();

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
        public static Cluster AddClustering(this ActorSystem actorSystem, string clusterName, IClusterProvider clusterProvider, Action<Cluster>? configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterName, clusterProvider);
            configure?.Invoke(cluster);
            return cluster;
        }
        public static Cluster AddClustering(this ActorSystem actorSystem, ClusterConfig clusterConfig, Action<Cluster>? configure = null)
        {
            var cluster = new Cluster(actorSystem, clusterConfig);
            configure?.Invoke(cluster);
            return cluster;
        }
        public static Task StartCluster(this ActorSystem actorSystem)
        {
            return actorSystem.Plugins.GetPlugin<Cluster>().StartAsync();
        }
        public static Task ShutdownClusterAsync(this ActorSystem actorSystem, bool graceful = true)
        {
            return actorSystem.Plugins.GetPlugin<Cluster>().ShutdownAsync(graceful);
        }
        public static Task<(PID?, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind)
        {
            var cluster = actorSystem.Plugins.GetPlugin<Cluster>();
            return cluster.GetAsync(name, kind);
        }
        public static Task<(PID?, ResponseStatusCode)> GetAsync(this ActorSystem actorSystem, string name, string kind, CancellationToken ct)
        {
            var cluster = actorSystem.Plugins.GetPlugin<Cluster>();
            return cluster.GetAsync(name, kind, ct);
        }
    }
}