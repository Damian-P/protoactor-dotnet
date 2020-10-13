// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;

namespace Proto.Remote
{
    public class GrpcNetChannelProvider : IChannelProvider
    {
        private readonly GrpcNetRemoteConfig _remoteConfig;
        public GrpcNetChannelProvider(GrpcNetRemoteConfig remoteConfig)
        {
            _remoteConfig = remoteConfig;
        }

        public ChannelBase GetChannel(string address)
        {
            var addressWithProtocol =
                $"{(_remoteConfig.UseHttps ? "https://" : "http://")}{address}";

            var channel = GrpcChannel.ForAddress(addressWithProtocol, _remoteConfig?.ChannelOptions ?? new GrpcChannelOptions());
            return channel;
        }
    }
}