// Copyright 2019, Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;

namespace FirebaseAdmin.Auth
{
    /// <summary>
    /// Contains a collection of Firebase user accounts.
    /// </summary>
    public sealed class ExportedUserRecords
    {
        /// <summary>
        /// Gets or sets the next page link.
        /// </summary>
        public string NextPageToken { get; set; }

        /// <summary>
        /// Gets or sets the users.
        /// </summary>
        public IEnumerable<ExportedUserRecord> Users { get; set; }
    }
}
