﻿// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;

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
                    Console.WriteLine($"Starting");
                    _sender = sr.Sender;
                    context.Respond(new Start());
                    return Actor.Done;
                case Ping _:
                    context.Send(_sender, new Pong());
                    return Actor.Done;
                default:
                    return Actor.Done;
            }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Log.SetLoggerFactory(LoggerFactory.Create(c => c
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Proto.EventStream", LogLevel.None)
            .AddConsole()
            ));
            var system = new ActorSystem();
            var context = new RootContext(system);
            var remote = system.AddRemote("127.0.0.1", 12000, remoteConfiguration =>
            {
                remoteConfiguration.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            });
            remote.Start();
            context.SpawnNamed(Props.FromProducer(() => new EchoActor()), "remote");
            Console.ReadLine();
            await remote.ShutdownAsync();
        }
    }
}