﻿// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2018 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Proto;
using Microsoft.Extensions.Logging;
using ProtosReflection = Messages.ProtosReflection;
using Proto.Remote;

namespace Node2
{
    public class EchoActor : IActor
    {
        private PID _sender;

        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case StartRemote sr:
                    Console.WriteLine("Starting");
                    _sender = sr.Sender;
                    context.Watch(sr.Sender);
                    context.Respond(new Start());
                    return Actor.Done;
                case Ping _:
                    context.Send(_sender, new Pong());
                    return Actor.Done;
                default:
                    Console.WriteLine(context.Message);
                    return Actor.Done;
            }
        }
    }

    static class Program
    {
        private static async Task Main(string[] args)
        {
            Log.SetLoggerFactory(LoggerFactory.Create(b => b.AddConsole()
                                                            .AddFilter("Microsoft", LogLevel.Critical)
                                                            .AddFilter("Grpc.AspNetCore", LogLevel.Critical)
                                                            .AddFilter("Proto.EventStream", LogLevel.Warning)
                                                            .AddFilter("Proto.Remote.EndpointActor", LogLevel.Debug)
                                                            .SetMinimumLevel(LogLevel.Information)));
            var system = new ActorSystem();
            var context = new RootContext(system);
            var Remote = new SelfHostedRemoteServerOverAspNet(system, "127.0.0.1", 12000, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("ponger", Props.FromProducer(() => new EchoActor()));
                remote.RemoteConfig.EndpointWriterOptions.MaxRetries = 5;
                remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan = TimeSpan.FromSeconds(10);
                remote.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize = 10000;
            });

            Remote.Start();
            system.Root.SpawnNamed(Props.FromProducer(() => new EchoActor()), "ponger");
            Console.ReadLine();
            await Remote.Stop();
        }
    }
}