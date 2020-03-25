// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using Grpc.Core;
using Grpc.Net.Client;

namespace Proto.Remote.AspNetCore
{
    internal class ChannelProvider : IChannelProvider
    {
        public ChannelBase GetChannel(RemoteConfig remoteConfig, string address)
        {
            var addressWithProtocol =
                $"{(remoteConfig.ChannelCredentials == ChannelCredentials.Insecure ? "http://" : "https://")}{address}";

            var channel = GrpcChannel.ForAddress(addressWithProtocol);

            return channel;
        }
    }
}