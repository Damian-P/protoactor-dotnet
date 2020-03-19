// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.AspNetCore
{
    public class RemoteActorSystem : RemoteActorSystemBase
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(RemoteActorSystem).FullName);
        private IHost _host;
        public new RemoteConfigBase RemoteConfig { get; }

        public RemoteActorSystem(string hostname, int port, RemoteConfig config = null) : base(hostname, port, config)
        {
            RemoteConfig = config ?? new RemoteConfig();
            // Allows tu use Grpc.Net over http
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            EndpointManager = new EndpointManager(this);
        }

        public override async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await base.StartAsync(cancellationToken);
            if (_host != null) throw new InvalidOperationException("Already started");
            _host = Host.CreateDefaultBuilder().UseConsoleLifetime().ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.ConfigureKestrel(serverOptions =>
                            {
                                serverOptions.Listen(IPAddress.Any, Port,
                                    listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; }
                                );
                            }
                        );
                        webBuilder.ConfigureLogging(builder =>
                            {
                                builder
                                    .SetMinimumLevel(LogLevel.Information)
                                    .AddConsole()
                                    .AddFilter("Grpc.AspNetCore.Server", LogLevel.Critical)
                                    .AddFilter("Microsoft", LogLevel.Critical);
                            }
                        );
                        webBuilder.ConfigureServices(serviceCollection =>
                            {
                                serviceCollection.AddGrpc();
                                serviceCollection.AddSingleton<IInternalRemoteActorAsystem>(sp => { return this; });
                                serviceCollection.AddSingleton<IActorSystem>(sp => { return this; });
                                serviceCollection.AddSingleton<EndpointReader>(sp => this.EndpointReader);
                            }
                        );
                        webBuilder.Configure((context, app) =>
                            {
                                Log.SetLoggerFactory(app.ApplicationServices.GetRequiredService<ILoggerFactory>());
                                var serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
                                var address = serverAddressesFeature.Addresses.FirstOrDefault();

                                app.UseRouting();

                                Console.WriteLine(string.Join(", ", serverAddressesFeature.Addresses));
                                app.UseEndpoints(endpoints => { endpoints.MapGrpcService<EndpointReader>(); });
                            }
                        );
                    }
                )
                .Build();
            await _host.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await base.StopAsync(cancellationToken);
            await _host.StopAsync(cancellationToken);
            _host.Dispose();
        }
    }
}