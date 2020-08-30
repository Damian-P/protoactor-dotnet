using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Proto;
using Proto.Remote;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Worker
{
    public class HelloGrain : IHelloGrain
    {
        public Task<HelloResponse> SayHello(HelloRequest request) => Task.FromResult(new HelloResponse { Message = "" });
    }

    public class Startup
    {
        public Startup(IConfiguration configuration, IHostEnvironment hostingEnvironment)
        {
            Configuration = configuration;
            HostingEnvironment = hostingEnvironment;
        }

        public IConfiguration Configuration { get; }
        public IHostEnvironment HostingEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddSeq(Configuration.GetSection("Seq"));
            });
            services.AddGrpc();
            services.AddProtoActor();
            services.AddRemote(remote =>
                {
                    remote.RemoteConfig.AdvertisedHostname =
                        Configuration.GetValue<string>("Proto_Hostname", Environment.MachineName);
                    remote.RemoteConfig.AdvertisedPort = 80;
                    remote.RemoteConfig.EndpointWriterOptions.MaxRetries = 2;
                    remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan = TimeSpan.FromHours(1);
                    remote.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                    var helloProps = Props.FromProducer(() => new HelloActor());
                    remote.RemoteKindRegistry.RegisterKnownKind("HelloActor", helloProps);
                }
            );
            services.AddClustering(
                "StabilityTestAsp",
                new ConsulProvider(new ConsulProviderOptions
                {
                    DeregisterCritical = TimeSpan.FromSeconds(2)
                },
                    c => { c.Address = new Uri($"http://consul:8500/"); }
                ), cluster =>
                {
                    var grains = new Grains(cluster);
                    grains.HelloGrainFactory(() => new HelloGrain());
                    services.AddSingleton(grains);
                }
            );
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            Log.SetLoggerFactory(loggerFactory);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
                {
                    endpoints.MapProtoRemoteService();
                    endpoints.MapGet("/",
                        async context =>
                        {
                            await context.Response.WriteAsync(
                                "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909"
                            );
                        }
                    );
                }
            );
        }
    }

    public class HelloActor : IActor
    {
        //   private readonly ILogger _log = Log.CreateLogger<HelloActor>();

        public Task ReceiveAsync(IContext ctx)
        {
            if (ctx.Message is Started)
            {
                Console.Write("#");
            }

            if (ctx.Message is HelloRequest)
            {
                ctx.Respond(new HelloResponse());
            }

            if (ctx.Message is Stopped)
            {
                Console.Write("T");
            }

            return Actor.Done;
        }
    }
}