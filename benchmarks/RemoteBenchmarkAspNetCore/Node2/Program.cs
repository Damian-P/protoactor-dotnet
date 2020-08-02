// -----------------------------------------------------------------------
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
using System.Collections.Concurrent;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.Collections.Generic;

namespace Node2
{
    public class MyJsonSerializer : Proto.Remote.ISerializer
    {
        public MyJsonSerializer()
        {
            RegisterFileDescriptor(Proto.ProtosReflection.Descriptor);
            RegisterFileDescriptor(Proto.Remote.ProtosReflection.Descriptor);
        }
        private readonly Dictionary<string, MessageParser> TypeLookup = new Dictionary<string, MessageParser>();
        private readonly Dictionary<string, Func<ByteString, object>> types = new Dictionary<string, Func<ByteString, object>>();
        public ByteString Serialize(object obj)
        {
            return obj switch
            {
                IMessage message when TypeLookup.ContainsKey(message.Descriptor.FullName) => message.ToByteString(),
                object o => ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(obj)),
                _ => throw new Exception("Type unknown")
            };
        }

        public object Deserialize(ByteString bytes, string typeName)
        {
            if (TypeLookup.TryGetValue(typeName, out var parser))
            {
                return parser.ParseFrom(bytes);
            }
            if (types.TryGetValue(typeName, out var deserialize))
            {
                return deserialize(bytes);
            }
            Console.WriteLine($"Json not registered: {typeName}");
            return System.Text.Json.JsonSerializer.Deserialize(bytes.ToStringUtf8(), Type.GetType(typeName));
        }

        public void RegisterFileDescriptor(FileDescriptor fd)
        {
            foreach (var msg in fd.MessageTypes)
            {
                TypeLookup.Add(msg.FullName, msg.Parser);
            }
        }

        public void RegisterTypeDeserializer<T>()
        {
            types.TryAdd(typeof(T).FullName, bytes => System.Text.Json.JsonSerializer.Deserialize<T>(bytes.ToStringUtf8()));
        }

        public string GetTypeName(object obj)
        {
            switch (obj)
            {
                case IMessage message when TypeLookup.ContainsKey(message.Descriptor.FullName):
                    return message.Descriptor.FullName;
                default:
                    return obj.GetType().FullName;
            }
        }
    }

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
                    break;
                case Ping _:
                    context.Send(_sender, new Pong());
                    break;
            }
            return Actor.Done;
        }
    }

    static class Program
    {
        private static async Task Main(string[] args)
        {
            Log.SetLoggerFactory(LoggerFactory.Create(b => b.AddConsole()
                                                            .AddFilter("Proto.EventStream", LogLevel.Critical)
                                                            .AddFilter("Microsoft", LogLevel.Critical)
                                                            .AddFilter("Grpc.AspNetCore", LogLevel.Critical)
                                                            .SetMinimumLevel(LogLevel.Information)));
            var system = new ActorSystem();
            var context = new RootContext(system);
            var Remote = new SelfHostedRemoteServerOverAspNet(system, "127.0.0.1", 12000, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("ponger", Props.FromProducer(() => new EchoActor()));
                remote.RemoteConfig.EndpointWriterOptions.MaxRetries = 2;
                remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan = TimeSpan.FromHours(1);
                var s = new MyJsonSerializer();
                s.RegisterTypeDeserializer<Messages.Ping>();
                s.RegisterTypeDeserializer<Messages.Pong>();
                s.RegisterTypeDeserializer<Messages.Start>();
                s.RegisterTypeDeserializer<Messages.StartRemote>();
                s.RegisterFileDescriptor(ProtosReflection.Descriptor);
                remote.Serialization.RegisterSerializer(s, true);
            });

            Remote.Start();
            system.Root.SpawnNamed(Props.FromProducer(() => new EchoActor()), "ponger");
            Console.ReadLine();
            await Remote.Stop();
        }
    }
}