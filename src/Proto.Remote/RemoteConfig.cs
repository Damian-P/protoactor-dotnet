﻿// -----------------------------------------------------------------------
//   <copyright file="RemoteConfig.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Grpc.Core;
using JetBrains.Annotations;

namespace Proto.Remote
{
    [PublicAPI]
    public class RemoteConfig
    {
        public const string AllInterfaces = "0.0.0.0";
        public const string Localhost = "127.0.0.1";
        public const int AnyFreePort = 0;
        protected RemoteConfig(string host, int port)
        {
            Host = host;
            Port = port;
        }

        /// <summary>
        ///     The host to listen to
        /// </summary>
        public string Host { get; }

        /// <summary>
        ///     The port to listen to, 0 means any free port
        /// </summary>
        public int Port { get; }

        /// <summary>
        ///     Known actor kinds that can be spawned remotely
        /// </summary>
        public Dictionary<string, Props> RemoteKinds { get; private set; } = new Dictionary<string, Props>();

        /// <summary>
        ///     Gets or sets the CallOptions for the gRPC channel.
        /// </summary>
        public CallOptions CallOptions { get; set; }

        /// <summary>
        ///     Gets or sets the advertised hostname for the remote system.
        ///     If the remote system is behind e.g. a NAT or reverse proxy, this needs to be set to
        ///     the external hostname in order for other systems to be able to connect to it.
        /// </summary>
        public string? AdvertisedHostname { get; set; }

        /// <summary>
        ///     Gets or sets the advertised port for the remote system.
        ///     If the remote system is behind e.g. a NAT or reverse proxy, this needs to be set to
        ///     the external port in order for other systems to be able to connect to it.
        /// </summary>
        public int? AdvertisedPort { get; set; }

        public EndpointWriterOptions EndpointWriterOptions { get; set; } = new EndpointWriterOptions();

        public Serialization Serialization { get; set; } = new Serialization();

        
    }
}