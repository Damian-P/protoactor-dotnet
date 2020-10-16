// -----------------------------------------------------------------------
//   <copyright file="AspRemoteConfig.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Proto.Remote
{
    public class GrpcNetRemoteConfig : RemoteConfig
    {
        GrpcNetRemoteConfig(string host, int port) : base(host, port)
        {

        }
        public static GrpcNetRemoteConfig BinToAllInterfaces(string advertisedHost, int port = 0) =>
            new GrpcNetRemoteConfig(AllInterfaces, port).WithAdvertisedHost(advertisedHost);
        
        public static GrpcNetRemoteConfig BindToLocalhost(int port = 0) => new GrpcNetRemoteConfig(Localhost, port);
        
        public static GrpcNetRemoteConfig BindTo(string host, int port = 0) => new GrpcNetRemoteConfig(host, port);
        public bool UseHttps { get; set; }
        public GrpcChannelOptions ChannelOptions { get; set; } = new GrpcChannelOptions();
        public Action<ListenOptions>? ConfigureKestrel { get; set; }
    }
}