// -----------------------------------------------------------------------
//   <copyright file="RemoteConfigExtensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Proto.Remote
{
    public static class RemoteConfigExtensions
    {
        public static string[] GetRemoteKinds<TRemoteConfig>(this TRemoteConfig remoteConfig)
        where TRemoteConfig : RemoteConfig
            => remoteConfig.RemoteKinds.Keys.ToArray();
        public static Props GetRemoteKind<TRemoteConfig>(this TRemoteConfig remoteConfig, string kind)
        where TRemoteConfig : RemoteConfig
        {
            if (!remoteConfig.RemoteKinds.TryGetValue(kind, out var props))
            {
                throw new ArgumentException($"No Props found for kind '{kind}'");
            }

            return props;
        }
        
        public static TRemoteConfig WithCallOptions<TRemoteConfig>(this TRemoteConfig remoteConfig, CallOptions options)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.CallOptions = options;
            return remoteConfig;
        }

        public static TRemoteConfig WithAdvertisedHost<TRemoteConfig>(this TRemoteConfig remoteConfig, string? advertisedHostname)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.AdvertisedHostname = advertisedHostname;
            return remoteConfig;
        }

        /// <summary>
        /// Advertised port can be different from the bound port, e.g. in container scenarios
        /// </summary>
        /// <param name="advertisedPort"></param>
        /// <returns></returns>
        public static TRemoteConfig WithAdvertisedPort<TRemoteConfig>(this TRemoteConfig remoteConfig, int? advertisedPort)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.AdvertisedPort = advertisedPort;
            return remoteConfig;
        }

        public static TRemoteConfig WithEndpointWriterBatchSize<TRemoteConfig>(this TRemoteConfig remoteConfig, int endpointWriterBatchSize)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.EndpointWriterOptions.EndpointWriterBatchSize = endpointWriterBatchSize;
            return remoteConfig;
        }

        public static TRemoteConfig WithEndpointWriterMaxRetries<TRemoteConfig>(this TRemoteConfig remoteConfig, int endpointWriterMaxRetries)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.EndpointWriterOptions.MaxRetries = endpointWriterMaxRetries;
            return remoteConfig;
        }

        public static TRemoteConfig WithEndpointWriterRetryTimeSpan<TRemoteConfig>(this TRemoteConfig remoteConfig, TimeSpan endpointWriterRetryTimeSpan)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.EndpointWriterOptions.RetryTimeSpan = endpointWriterRetryTimeSpan;
            return remoteConfig;
        }

        public static TRemoteConfig WithEndpointWriterRetryBackOff<TRemoteConfig>(this TRemoteConfig remoteConfig, TimeSpan endpointWriterRetryBackoff)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.EndpointWriterOptions.RetryBackOff = endpointWriterRetryBackoff;
            return remoteConfig;
        }

        public static TRemoteConfig WithProtoMessages<TRemoteConfig>(this TRemoteConfig remoteConfig, params FileDescriptor[] fileDescriptors)
        where TRemoteConfig : RemoteConfig
        {
            foreach (var fd in fileDescriptors) remoteConfig.Serialization.RegisterFileDescriptor(fd);
            return remoteConfig;
        }

        public static TRemoteConfig WithRemoteKind<TRemoteConfig>(this TRemoteConfig remoteConfig, string kind, Props prop)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.RemoteKinds.Add(kind, prop);
            return remoteConfig;
        }

        public static TRemoteConfig WithRemoteKinds<TRemoteConfig>(this TRemoteConfig remoteConfig, params (string kind, Props prop)[] knownKinds)
        where TRemoteConfig : RemoteConfig
        {
            foreach (var (kind, prop) in knownKinds) remoteConfig.RemoteKinds.Add(kind, prop);
            return remoteConfig;
        }
        
        public static TRemoteConfig WithSerializer<TRemoteConfig>(this TRemoteConfig remoteConfig, ISerializer serializer, bool makeDefault = false)
        where TRemoteConfig : RemoteConfig
        {
            remoteConfig.Serialization.RegisterSerializer(serializer,makeDefault);
            return remoteConfig;
        }
    }
}