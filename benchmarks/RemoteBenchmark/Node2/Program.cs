// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;
using Microsoft.Extensions.Logging;

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
                    context.Respond(new Start());
                    return Task.CompletedTask;
                case Ping _:
                    context.Send(_sender, new Pong());
                    return Task.CompletedTask;
                default:
                    return Task.CompletedTask;
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
            var remoteConfig =  RemoteConfig.BindToLocalhost(12000).WithProtoMessages(ProtosReflection.Descriptor);
            var remote = new SelfHostedRemote(system, remoteConfig);
            await remote.StartAsync();
            context.SpawnNamed(Props.FromProducer(() => new EchoActor()), "remote");
            Console.ReadLine();
            await remote.ShutdownAsync();
        }
    }
}