// -----------------------------------------------------------------------
//   <copyright file="RemoteHostedService.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    internal class RemoteHostedService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IRemote _remote;

        public RemoteHostedService(
            ILogger<RemoteHostedService> logger,
            IHostApplicationLifetime appLifetime,
            IRemote remote)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _remote = remote;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping, true);
            _appLifetime.ApplicationStarted.Register(OnStarted, true);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _remote.Start();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _remote.ShutdownAsync().GetAwaiter().GetResult();
        }
    }
}