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
using System.Net.Http;
using Google.Apis.Json;
using Newtonsoft.Json;

namespace FirebaseAdmin
{
    internal class PlatformErrorHandler : HttpErrorHandler
    {
        private static readonly IReadOnlyDictionary<string, ErrorCode> PlatformErrorCodes =
            new Dictionary<string, ErrorCode>()
            {
                { "INVALID_ARGUMENT", ErrorCode.InvalidArgument },
                { "INTERNAL", ErrorCode.Internal },
                { "PERMISSION_DENIED", ErrorCode.PermissionDenied },
                { "UNAUTHENTICATED", ErrorCode.Unauthenticated },
                { "UNAVAILABLE", ErrorCode.Unavailable },
            };

        protected sealed override ErrorInfo ExtractErrorInfo(HttpResponseMessage response, string body)
        {
            var parsedResponse = this.ParseResponseBody(body);
            var status = parsedResponse.Error?.Status ?? string.Empty;
            var defaults = base.ExtractErrorInfo(response, body);

            ErrorCode code;
            if (!PlatformErrorCodes.TryGetValue(status, out code))
            {
                code = defaults.Code;
            }

            var message = parsedResponse.Error?.Message;
            if (string.IsNullOrEmpty(message))
            {
                message = defaults.Message;
            }

            return new ErrorInfo()
            {
                Code = code,
                Message = message,
            };
        }

        private PlatformErrorResponse ParseResponseBody(string body)
        {
            try
            {
                return NewtonsoftJsonSerializer.Instance.Deserialize<PlatformErrorResponse>(body);
            }
            catch
            {
                // Ignore any error that may occur while parsing the error response. The server
                // may have responded with a non-json payload. Return an empty return value, and
                // let the base class logic come into play.
                return new PlatformErrorResponse();
            }
        }

        internal sealed class PlatformErrorResponse
        {
            [JsonProperty("error")]
            public PlatformError Error { get; set; }
        }

        internal class PlatformError
        {
            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }
    }
}
