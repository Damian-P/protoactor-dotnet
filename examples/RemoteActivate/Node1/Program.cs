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

class Program
{
    static async Task Main(string[] args)
    {
        var system = new ActorSystem();
        system.AddRemoteOverGrpc("127.0.0.1", 12001, remote =>
        {
            remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        });
        system.StartRemote();
        var pid = system.SpawnNamedAsync("127.0.0.1:12000", "remote", "hello", TimeSpan.FromSeconds(5)).Result.Pid;
        var res = await system.Root.RequestAsync<HelloResponse>(pid, new HelloRequest { });
        Console.WriteLine(res.Message);
        Console.ReadLine();
    }
}
