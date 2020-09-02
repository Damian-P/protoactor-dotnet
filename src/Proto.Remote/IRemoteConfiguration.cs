// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------
// Modified file in context of repo fork : https://github.com/Optis-World/protoactor-dotnet
// Copyright 2019 ANSYS, Inc.
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Proto.Remote
{
    public interface IRemoteConfiguration<TRemoteConfig> : IRemoteConfiguration
    where TRemoteConfig : RemoteConfig, new()
    {
        new TRemoteConfig RemoteConfig { get; }
    }
    public interface IRemoteConfiguration
    {
        RemoteConfig RemoteConfig { get; }
        RemoteKindRegistry RemoteKindRegistry { get; }
        Serialization Serialization { get; }
    }
}