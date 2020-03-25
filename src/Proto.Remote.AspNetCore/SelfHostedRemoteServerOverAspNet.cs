// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemoteServerOverAspNet.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using Proto.Remote.AspNetCore;

namespace Proto.Remote
{
    public class SelfHostedRemoteServerOverAspNet : Remote
    {
        private IHost _host;
        public SelfHostedRemoteServerOverAspNet(ActorSystem system, string hostname, int port, Action<RemotingConfiguration> configure = null)
        : base(system, new ChannelProvider(), hostname, port, configure)
        {

        }
        public override async Task Start()
        {
            IServerAddressesFeature serverAddressesFeature = null;
            await base.Start();
            // Allows tu use Grpc.Net over http
            if (_remote.RemoteConfig.ServerCredentials == ServerCredentials.Insecure)
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var endpointReader = new EndpointReader(_system, _endpointManager, _remote.Serialization);
            if (_host != null) throw new InvalidOperationException("Already started");
            _host = await Host
                .CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                        webBuilder
                        .ConfigureKestrel(serverOptions =>
                            {
                                if (_remote.RemoteConfig.ServerCredentials == ServerCredentials.Insecure)
                                    serverOptions.Listen(IPAddress.Any, _port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
                                else
                                    serverOptions.Listen(IPAddress.Any, _port, listenOptions =>
                                    {
                                        listenOptions.Protocols = HttpProtocols.Http2;
                                        listenOptions.UseHttps();
                                    });
                            }
                        )
                        .ConfigureServices((context, serviceCollection) =>
                            {
                                serviceCollection.AddGrpc();
                                serviceCollection.AddHostedService<SelfHostedRemoteService>();
                                serviceCollection.AddSingleton<ILoggerFactory>(Log.LoggerFactory);
                                serviceCollection.AddSingleton(_endpointManager);
                                serviceCollection.AddSingleton<RemoteConfig>(_remote.RemoteConfig);
                                serviceCollection.AddSingleton<ActorSystem>(sp => _system);
                                serviceCollection.AddSingleton<Remoting.RemotingBase>(sp => endpointReader);
                            }
                        ).
                        Configure((app) =>
                            {
                                app.UseRouting();
                                app.UseEndpoints(endpoints =>
                                {
                                    endpoints.MapGrpcService<Remoting.RemotingBase>();
                                });
                                serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
                            }
                        )
                )
                .StartAsync();

            var boundPort = serverAddressesFeature.Addresses.Select(a => int.Parse(a.Split(":")[2])).First();
            _system.ProcessRegistry.SetAddress(_remote.RemoteConfig.AdvertisedHostname ?? _hostname, _remote.RemoteConfig.AdvertisedPort ?? boundPort);
            Logger.LogInformation("Starting Proto.Actor server on {Host}:{Port} ({Address})", _hostname, boundPort,
                _system.ProcessRegistry.Address
            );
        }
        public override async Task Stop(bool graceful = true)
        {
            using (_host)
            {
                try
                {
                    if (graceful)
                    {
                        // await base.Stop();
                        await _host.StopAsync();
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