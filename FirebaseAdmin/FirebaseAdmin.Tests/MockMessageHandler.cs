// Copyright 2018, Google Inc. All rights reserved.
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

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Json;

namespace FirebaseAdmin.Tests
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> implementation that counts the number of requests
    /// and facilitates mocking HTTP interactions locally.
    /// </summary>
    internal sealed class MockMessageHandler : CountingMessageHandler
    {
        public MockMessageHandler()
        {
            this.StatusCode = HttpStatusCode.OK;
        }

        public delegate void SetHeaders(HttpResponseHeaders respHeaders, HttpContentHeaders contentHeaders);

        /// <summary>
        /// Gets the body of the last request processed by this handler.
        /// </summary>
        public string RequestBody { get; private set; }

        /// <summary>
        /// Gets the headers of the last request processed by this handler.
        /// </summary>
        public HttpRequestHeaders RequestHeaders { get; private set; }

        /// <summary>
        /// Gets or sets the HTTP status code that should be set on the responses generated by this handler.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Gets or sets the HTTP response body that should be send by this handler. This can be specified
        /// as a byte array, a string, or a JSON-serializable object.
        /// </summary>
        public object Response { get; set; }

        /// <summary>
        /// Gets or sets the function for modifying the response headers.
        /// </summary>
        public SetHeaders ApplyHeaders { get; set; }

        protected override async Task<HttpResponseMessage> DoSendAsync(
            HttpRequestMessage request, int count, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                this.RequestBody = await request.Content.ReadAsStringAsync();
            }
            else
            {
                this.RequestBody = null;
            }

            this.RequestHeaders = request.Headers;

            string json;
            if (this.Response is byte[])
            {
                json = Encoding.UTF8.GetString(this.Response as byte[]);
            }
            else if (this.Response is string)
            {
                json = this.Response as string;
            }
            else
            {
                json = NewtonsoftJsonSerializer.Instance.Serialize(this.Response);
            }

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = new HttpResponseMessage();
            resp.StatusCode = this.StatusCode;
            resp.Content = content;
            if (this.ApplyHeaders != null)
            {
                this.ApplyHeaders(resp.Headers, content.Headers);
            }

            var tcs = new TaskCompletionSource<HttpResponseMessage>();
            tcs.SetResult(resp);
            return await tcs.Task;
        }
    }
}
