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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Json;
using Xunit;

namespace FirebaseAdmin.Tests
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> implementation that counts the number of requests
    /// and facilitates mocking HTTP interactions locally.
    /// </summary>
    internal class MockMessageHandler : CountableMessageHandler
    {
        public MockMessageHandler()
        {
            this.StatusCode = HttpStatusCode.OK;
        }

        public delegate void SetHeaders(HttpResponseHeaders header);

        public delegate void SetContentHeaders(HttpContentHeaders header);

        public string Request { get; private set; }

        public HttpRequestHeaders RequestHeaders { get; private set; }

        public HttpStatusCode StatusCode { get; set; }

        public object Response { get; set; }

        public SetHeaders ApplyHeaders { get; set; }

        public SetContentHeaders ApplyContentHeaders { get; set; }

        protected internal override async Task<HttpResponseMessage> SendAsyncCore(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                this.Request = await request.Content.ReadAsStringAsync();
            }
            else
            {
                this.Request = null;
            }

            this.RequestHeaders = request.Headers;

            var resp = new HttpResponseMessage();
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

            resp.StatusCode = this.StatusCode;
            if (this.ApplyHeaders != null)
            {
                this.ApplyHeaders(resp.Headers);
            }

            var responseContent = new StringContent(json, Encoding.UTF8, "application/json");

            if (this.ApplyContentHeaders != null)
            {
                this.ApplyContentHeaders(responseContent.Headers);
            }

            resp.Content = responseContent;

            var tcs = new TaskCompletionSource<HttpResponseMessage>();
            tcs.SetResult(resp);
            return await tcs.Task;
        }
    }

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> implementation that counts the number of requests
    /// processed.
    /// </summary>
    internal abstract class CountableMessageHandler : HttpMessageHandler
    {
        private int calls;

        public int Calls
        {
            get { return this.calls; }
        }

        protected internal abstract Task<HttpResponseMessage> SendAsyncCore(
            HttpRequestMessage request, CancellationToken cancellationToken);

        protected sealed override Task<HttpResponseMessage> SendAsync(
          HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.calls);
            return this.SendAsyncCore(request, cancellationToken);
        }
    }

    internal class MultipleMockMessageHandler : CountableMessageHandler
    {
        private readonly IDictionary<Func<HttpRequestMessage, bool>, MockMessageHandler> messageHandlers;

        public MultipleMockMessageHandler(IDictionary<Func<HttpRequestMessage, bool>, MockMessageHandler> messageHandlers)
        {
            this.messageHandlers = messageHandlers;
        }

        protected internal override async Task<HttpResponseMessage> SendAsyncCore(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var (requestCheck, mockMessageHandler) in this.messageHandlers)
            {
                // check if the messagehandler is responsible for the current request
                if (requestCheck.Invoke(request))
                {
                    this.messageHandlers.Remove(requestCheck);
                    return await mockMessageHandler.SendAsyncCore(request, cancellationToken);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    internal class MockHttpClientFactory : HttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public MockHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
        {
            return this.handler;
        }
    }
}
