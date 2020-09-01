// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.HealthCheck;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class SelfHostedRemote : Remote
    {
        private IWebHost? _host;
        private readonly Action<ListenOptions>? configureKestrel;

        public SelfHostedRemote(ActorSystem system, string hostname, int port,
            Action<IRemoteConfiguration>? configure = null, Action<GrpcChannelOptions>? configureChannelOptions = null, Action<ListenOptions>? configureKestrel = null)
            : base(system, hostname, port, new ChannelProvider(configureChannelOptions), configure)
        {
            this.configureKestrel = configureKestrel;
        }

        public override void Start()
        {
            if (IsStarted) return;
            IServerAddressesFeature? serverAddressesFeature = null;
            base.Start();
            // Allows tu use Grpc.Net over http
            if (configureKestrel == null)
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var endpointReader = new EndpointReader(_system, EndpointManager, Serialization);
            if (_host != null) throw new InvalidOperationException("Already started");

            _host = new WebHostBuilder()
                .UseKestrel()
                .ConfigureKestrel(serverOptions =>
                    {
                        if (configureKestrel == null)
                            serverOptions.Listen(IPAddress.Any, _port,
                                listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; }
                            );
                        else
                            serverOptions.Listen(IPAddress.Any, _port,
                                listenOptions => configureKestrel(listenOptions)
                            );
                    }
                )
                .ConfigureServices((serviceCollection) =>
                    {
                        serviceCollection.AddGrpc();
                        serviceCollection.AddSingleton<ILoggerFactory>(Log.LoggerFactory);
                        serviceCollection.AddSingleton(EndpointManager);
                        serviceCollection.AddSingleton<RemoteConfig>(RemoteConfig);
                        serviceCollection.AddSingleton<ActorSystem>(sp => _system);
                        serviceCollection.AddSingleton<Remoting.RemotingBase>(sp => endpointReader);
                        serviceCollection.AddSingleton<IRemote>(this);
                    }
                ).Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGrpcService<Remoting.RemotingBase>();
                            endpoints.MapGrpcService<HealthServiceImpl>();
                        });
                        serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
                    }
                )
                .Start();

            var boundPort = serverAddressesFeature!.Addresses.Select(a => int.Parse(a.Split(":")[2])).First();
            _system.ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? _hostname,
                    RemoteConfig.AdvertisedPort ?? boundPort
                );
            Logger.LogInformation("Starting Proto.Actor server on {Host}:{Port} ({Address})", _hostname, boundPort,
                _system.ProcessRegistry.Address
            );
        }

        public override async Task ShutdownAsync(bool graceful = true)
        {
            using (_host)
            {
                try
                {
                    if (graceful)
                    {
                        await base.ShutdownAsync(graceful);
                    }
                    Logger.LogDebug(
                        "Proto.Actor server stopped on {Address}. Graceful: {Graceful}",
                        _system.ProcessRegistry.Address, graceful
                    );
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                        _system.ProcessRegistry.Address, ex.Message
                    );
                    throw;
                }
            }
        }
    }
}