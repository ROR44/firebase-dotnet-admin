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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Discovery;
using Google.Apis.Json;
using Google.Apis.Requests;
using Google.Apis.Services;

namespace FirebaseAdmin.Auth
{
    /// <summary>
    /// Represents a request made using the Google API client to list all Firebase users in a
    /// project.
    /// </summary>
    internal class ListUsersRequest : IClientServiceRequest<ExportedUserRecords>
    {
        // This is the default page-size if no other value is set.
        private const int MaxListUsersResults = 1000;

        private readonly string baseUrl;
        private readonly HttpClient httpClient;
        private readonly ListUsersOptions options;

        internal ListUsersRequest(string baseUrl, HttpClient httpClient, ListUsersOptions options)
        {
            this.baseUrl = baseUrl;
            this.httpClient = httpClient;
            this.options = new ListUsersOptions(options);
            this.RequestParameters = new Dictionary<string, IParameter>();
            this.SetPageSize(this.options.PageSize ?? MaxListUsersResults);

            if (this.options.PageToken == string.Empty)
            {
                throw new ArgumentException("Starting page token must not be empty.");
            }
            else if (this.options.PageToken != null)
            {
                this.SetPageToken(this.options.PageToken);
            }
        }

        public string MethodName => "ListUsers";

        public string RestPath => "accounts:batchGet";

        public string HttpMethod => "GET";

        public IDictionary<string, IParameter> RequestParameters { get; }

        public IClientService Service { get; }

        public void SetPageSize(int pageSize)
        {
            if (pageSize > MaxListUsersResults)
            {
                throw new ArgumentException("Page size must not exceed 1000.");
            }
            else if (pageSize <= 0)
            {
                throw new ArgumentException("Page size must be a positive integer.");
            }

            this.AddOrUpdate("maxResults", pageSize.ToString());
        }

        public void SetPageToken(string pageToken)
        {
            this.AddOrUpdate("nextPageToken", pageToken);
            this.options.PageToken = pageToken;
        }

        public HttpRequestMessage CreateRequest(bool? overrideGZipEnabled = null)
        {
            var queryParameters = string.Join("&", this.RequestParameters.Select(
                kvp => $"{kvp.Key}={kvp.Value.DefaultValue}"));
            return new HttpRequestMessage()
            {
                Method = System.Net.Http.HttpMethod.Get,
                RequestUri = new Uri($"{this.baseUrl}/{this.RestPath}?{queryParameters}"),
            };
        }

        public Task<Stream> ExecuteAsStreamAsync()
        {
            return this.ExecuteAsStreamAsync(default);
        }

        public Task<Stream> ExecuteAsStreamAsync(CancellationToken cancellationToken)
        {
            var response = this.SendAsync(this.CreateRequest(), cancellationToken);
            return response.Result.Content.ReadAsStreamAsync();
        }

        public Stream ExecuteAsStream()
        {
            return this.ExecuteAsStreamAsync().Result;
        }

        public Task<ExportedUserRecords> ExecuteAsync()
        {
            return this.ExecuteAsync(default);
        }

        public async Task<ExportedUserRecords> ExecuteAsync(CancellationToken cancellationToken)
        {
            var downloadAccountResponse = await this.SendAndDeserializeAsync(
                this.CreateRequest(), cancellationToken);
            return ConvertToExportedUserRecords(downloadAccountResponse);
        }

        public ExportedUserRecords Execute()
        {
            return this.ExecuteAsync().Result;
        }

        private static ExportedUserRecords ConvertToExportedUserRecords(
            DownloadAccountResponse downloadAccountResponse)
        {
            var userRecords = downloadAccountResponse.Users?.Select(u => new ExportedUserRecord(u));
            return new ExportedUserRecords
            {
                NextPageToken = downloadAccountResponse.NextPageToken,
                Users = userRecords,
            };
        }

        private async Task<DownloadAccountResponse> SendAndDeserializeAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await this.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = "Response status code does not indicate success: "
                            + $"{(int)response.StatusCode} ({response.StatusCode})"
                            + $"{Environment.NewLine}{json}";
                throw new FirebaseException(error);
            }

            return this.SafeDeserialize(json);
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await this.httpClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw new FirebaseException("Error while calling Firebase Auth service", e);
            }
        }

        private DownloadAccountResponse SafeDeserialize(string json)
        {
            try
            {
                return NewtonsoftJsonSerializer.Instance.Deserialize<DownloadAccountResponse>(json);
            }
            catch (Exception e)
            {
                throw new FirebaseException("Error while parsing Auth service response", e);
            }
        }

        private void AddOrUpdate(string paramName, string value)
        {
            var parameter = new Parameter()
            {
                DefaultValue = value,
                IsRequired = true,
                Name = paramName,
            };

            if (!this.RequestParameters.ContainsKey(paramName))
            {
                this.RequestParameters.Add(paramName, parameter);
            }
            else
            {
                this.RequestParameters[paramName] = parameter;
            }
        }
    }
}
