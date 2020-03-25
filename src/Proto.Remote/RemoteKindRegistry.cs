// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Proto.Remote
{
    public class RemoteKindRegistry
    {
        private readonly Dictionary<string, Props> _kinds = new Dictionary<string, Props>();
        public string[] GetKnownKinds() => _kinds.Keys.ToArray();
        public void RegisterKnownKind(string kind, Props props) => _kinds.Add(kind, props);
        public void UnregisterKnownKind(string kind) => _kinds.Remove(kind);
        public Props GetKnownKind(string kind)
        {
            if (_kinds.TryGetValue(kind, out var props))
            {
                return props;
            }

            throw new ArgumentException($"No Props found for kind '{kind}'");
        }
    }
}