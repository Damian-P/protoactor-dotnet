// -----------------------------------------------------------------------
//   <copyright file="HostedRemoteService.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    internal class HostedRemoteService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IRemote _remote;

        public HostedRemoteService(
            ILogger<HostedRemoteService> logger,
            IHostApplicationLifetime appLifetime,
            IRemote remote)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _remote = remote;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);
            _appLifetime.ApplicationStarted.Register(OnStarted);
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
            _remote.Stop().GetAwaiter().GetResult();
        }
    }
}