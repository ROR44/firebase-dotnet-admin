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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Requests;
using Google.Apis.Services;
using Google.Apis.Util;
using Newtonsoft.Json;

namespace FirebaseAdmin.Messaging
{
    /// <summary>
    /// A client for making authorized HTTP calls to the FCM backend service. Handles request
    /// serialization, response parsing, and HTTP error handling.
    /// </summary>
    internal sealed class FirebaseMessagingClient : IDisposable
    {
        private const string FcmBaseUrl = "https://fcm.googleapis.com";
        private const string FcmSendUrl = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";
        private const string FcmBatchUrl = "https://fcm.googleapis.com/batch";

        private readonly ConfigurableHttpClient httpClient;
        private readonly string sendUrl;

        public FirebaseMessagingClient(
            HttpClientFactory clientFactory, GoogleCredential credential, string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                throw new FirebaseException(
                    "Project ID is required to access messaging service. Use a service account "
                    + "credential or set the project ID explicitly via AppOptions. Alternatively "
                    + "you can set the project ID via the GOOGLE_CLOUD_PROJECT environment "
                    + "variable.");
            }

            this.httpClient = clientFactory.ThrowIfNull(nameof(clientFactory))
                .CreateAuthorizedHttpClient(credential);
            this.sendUrl = string.Format(FcmSendUrl, projectId);
        }

        /// <summary>
        /// Sends a message to the FCM service for delivery. The message gets validated both by
        /// the Admin SDK, and the remote FCM service. A successful return value indicates
        /// that the message has been successfully sent to FCM, where it has been accepted by the
        /// FCM service.
        /// </summary>
        /// <returns>A task that completes with a message ID string, which represents
        /// successful handoff to FCM.</returns>
        /// <exception cref="ArgumentNullException">If the message argument is null.</exception>
        /// <exception cref="ArgumentException">If the message contains any invalid
        /// fields.</exception>
        /// <exception cref="FirebaseException">If an error occurs while sending the
        /// message.</exception>
        /// <param name="message">The message to be sent. Must not be null.</param>
        /// <param name="dryRun">A boolean indicating whether to perform a dry run (validation
        /// only) of the send. If set to true, the message will be sent to the FCM backend service,
        /// but it will not be delivered to any actual recipients.</param>
        /// <param name="cancellationToken">A cancellation token to monitor the asynchronous
        /// operation.</param>
        public async Task<string> SendAsync(
            Message message,
            bool dryRun = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = new SendRequest()
            {
                Message = message.ThrowIfNull(nameof(message)).CopyAndValidate(),
                ValidateOnly = dryRun,
            };
            try
            {
                var response = await this.httpClient.PostJsonAsync(
                    this.sendUrl, request, cancellationToken).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var error = "Response status code does not indicate success: "
                            + $"{(int)response.StatusCode} ({response.StatusCode})"
                            + $"{Environment.NewLine}{json}";
                    throw new FirebaseException(error);
                }

                var parsed = JsonConvert.DeserializeObject<SendResponse>(json);
                return parsed.Name;
            }
            catch (HttpRequestException e)
            {
                throw new FirebaseException("Error while calling the FCM service.", e);
            }
        }

        /// <summary>
        /// Sends all messages in a single batch.
        /// </summary>
        /// <param name="messages">The messages to be sent. Must not be null.</param>
        /// <param name="dryRun">A boolean indicating whether to perform a dry run (validation
        /// only) of the send. If set to true, the messages will be sent to the FCM backend service,
        /// but it will not be delivered to any actual recipients.</param>
        /// <param name="cancellationToken">A cancellation token to monitor the asynchronous
        /// operation.</param>
        /// <returns>A task that completes with a <see cref="BatchResponse"/>, giving details about
        /// the batch operation.</returns>
        public async Task<BatchResponse> SendAllAsync(
            IEnumerable<Message> messages,
            bool dryRun = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            try
            {
                return await this.SendBatchRequestAsync(messages, dryRun, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                throw new FirebaseException("Error while calling the FCM service.", e);
            }
        }

        public void Dispose()
        {
            this.httpClient.Dispose();
        }

        private static FirebaseMessagingException CreateExceptionFor(RequestError requestError)
        {
            return new FirebaseMessagingException(requestError.Code, requestError.ToString());
        }

        private async Task<BatchResponse> SendBatchRequestAsync(IEnumerable<Message> messages, bool dryRun, CancellationToken cancellationToken)
        {
            var responses = new List<BatchItemResponse>();

            var batch = this.CreateBatchRequest(
                messages,
                dryRun,
                (content, error, index, message) =>
                {
                    if (error != null)
                    {
                        responses.Add(BatchItemResponse.FromException(CreateExceptionFor(error)));
                    }
                    else if (content != null)
                    {
                        responses.Add(BatchItemResponse.FromMessageId(content.Name));
                    }
                    else
                    {
                        responses.Add(BatchItemResponse.FromException(new FirebaseException($"Something went wrong processing a batch item. The response status code was {message.StatusCode}.")));
                    }
                });

            await batch.ExecuteAsync(cancellationToken);
            return new BatchResponse(responses);
        }

        private BatchRequest CreateBatchRequest(IEnumerable<Message> messages, bool dryRun, BatchRequest.OnResponse<SendResponse> callback)
        {
            var clientService = new FCMClientService(this.httpClient);
            var batch = new BatchRequest(clientService, FcmBatchUrl);

            foreach (var message in messages)
            {
                batch.Queue(new FCMClientServiceRequest(clientService, this.sendUrl.Substring(FcmBaseUrl.Length), message, dryRun), callback);
            }

            return batch;
        }

        /// <summary>
        /// Represents the envelope message accepted by the FCM backend service, including the message
        /// payload and other options like <c>validate_only</c>.
        /// </summary>
        internal class SendRequest
        {
            [Newtonsoft.Json.JsonProperty("message")]
            public Message Message { get; set; }

            [Newtonsoft.Json.JsonProperty("validate_only")]
            public bool ValidateOnly { get; set; }
        }

        /// <summary>
        /// Represents the response messages sent by the FCM backend service. Primarily consists of the
        /// message ID (Name) that indicates success handoff to FCM.
        /// </summary>
        internal class SendResponse
        {
            [Newtonsoft.Json.JsonProperty("name")]
            public string Name { get; set; }
        }

        private sealed class FCMClientService : BaseClientService
        {
            public FCMClientService(ConfigurableHttpClient httpClient)
                : base(new Initializer { HttpClientFactory = new CannedHttpClientFactory(httpClient) })
            {
            }

            public override string Name => "FCM";

            public override string BaseUri => "https://fcm.googleapis.com";

            public override string BasePath => null;

            public override IList<string> Features => null;
        }

        private sealed class CannedHttpClientFactory : IHttpClientFactory
        {
            private readonly ConfigurableHttpClient httpClient;

            public CannedHttpClientFactory(ConfigurableHttpClient httpClient)
            {
                this.httpClient = httpClient;
            }

            public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
            {
                return this.httpClient;
            }
        }

        private sealed class FCMClientServiceRequest : ClientServiceRequest<string>
        {
            private readonly string restPath;
            private readonly Message message;
            private readonly bool dryRun;

            public FCMClientServiceRequest(IClientService clientService, string restPath, Message message, bool dryRun)
                : base(clientService)
            {
                this.restPath = restPath;
                this.message = message;
                this.dryRun = dryRun;

                this.InitParameters();
            }

            public override string HttpMethod => "POST";

            public override string RestPath => this.restPath;

            public override string MethodName => throw new NotImplementedException();

            protected override object GetBody()
            {
                var sendRequest = new SendRequest
                {
                    Message = this.message,
                    ValidateOnly = this.dryRun,
                };

                return sendRequest;
            }

            protected override void InitParameters()
            {
                base.InitParameters();
            }
        }
    }
}
