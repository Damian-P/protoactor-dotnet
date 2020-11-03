// -----------------------------------------------------------------------
//   <copyright file="GrpcRemoteConfig.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Grpc.Core;

namespace Proto.Remote
{
    public record GrpcRemoteConfig : RemoteConfig
    {
        protected GrpcRemoteConfig(string host, int port) : base(host, port)
        {

        }
        public static GrpcRemoteConfig BinToAllInterfaces(string advertisedHost, int port = 0) =>
            new GrpcRemoteConfig(AllInterfaces, port).WithAdvertisedHost(advertisedHost);
        
        public static GrpcRemoteConfig BindToLocalhost(int port = 0) => new GrpcRemoteConfig(Localhost, port);
        
        public static GrpcRemoteConfig BindTo(string host, int port = 0) => new GrpcRemoteConfig(host, port);
        /// <summary>
        ///     Gets or sets the ChannelOptions for the gRPC channel.
        /// </summary>
        public IEnumerable<ChannelOption> ChannelOptions { get; init; } = null!;

        /// <summary>
        ///     Gets or sets the ChannelCredentials for the gRPC channel. The default is Insecure.
        /// </summary>
        public ChannelCredentials ChannelCredentials { get; init; } = ChannelCredentials.Insecure;

        /// <summary>
        ///     Gets or sets the ServerCredentials for the gRPC server. The default is Insecure.
        /// </summary>
        public ServerCredentials ServerCredentials { get; init; } = ServerCredentials.Insecure;
    }
}