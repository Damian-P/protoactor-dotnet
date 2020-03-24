// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2017 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;

namespace Node2
{

    class Program
    {
        static async Task Main(string[] args)
        {

            var props = Props.FromFunc(ctx =>
            {
                switch (ctx.Message)
                {
                    case HelloRequest msg:
                        ctx.Respond(new HelloResponse
                        {
                            Message = "Hello from node 2",
                        });
                        break;
                    default:
                        break;
                }
                return Actor.Done;
            });

            var system = new ActorSystem();
            system.AddRemotingOverGrpc("127.0.0.1", 12000, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("hello", props);
            });
            await system.StartRemote();

            Console.ReadLine();
        }
    }
}