﻿// ------------------------------------------------------------
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------
using Microsoft.ServiceFabric.Services.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ServiceFabric.Http.Client
{   
    /// <summary>
    /// HttpServiceClientHandler is a HTTP handler that can resolve HTTP endpoint from Service Fabric reliable services.
    /// It accounts for transient errors while Service Fabric runtime moves a service. When a transient error is observed, 
    /// HttpServiceClientHandler will perform another endpoint resolution and resend the request. Optionally caller can 
    /// customize when to retry the endpoint resolution by specifying the shouldResolve delegate. 
    /// </summary>
    public sealed class HttpServiceClientHandler : DelegatingHandler
    {
        /// <summary>
        /// Initialize a new instance of HttpServiceClientHandler.
        /// </summary>
        /// <param name="maxRetries">The max number of times to retry service endpoint resolution.</param>
        /// <param name="initialRetryDelayMs">The initial delay between service endpoint resolution, in milliseconds.</param>
        public HttpServiceClientHandler(int maxRetries = 5,
                                        int initialRetryDelayMs = 25) : base()
        {
            this.maxRetries = maxRetries;
            this.initialRetryDelayMs = initialRetryDelayMs;
        }

        /// <summary>
        /// Initialize a new instance of HttpServiceClientHandler.
        /// </summary>
        /// <param name="innerHandler">The inner handler.</param>
        /// <param name="maxRetries">The max number of times to retry service endpoint resolution.</param>
        /// <param name="initialRetryDelayMs">The initial delay between service endpoint resolution, in milliseconds.</param>
        public HttpServiceClientHandler(HttpMessageHandler innerHandler, 
                                        int maxRetries = 5, 
                                        int initialRetryDelayMs = 25):base(innerHandler)
        {
            this.maxRetries = maxRetries;
            this.initialRetryDelayMs = initialRetryDelayMs;
        }

        #region DelegatingHandler override
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // If the request is outside of the Fabric application, just pass through and do nothing
            if (!StringComparer.OrdinalIgnoreCase.Equals(request.RequestUri.Host, "fabric"))
            {
                return await base.SendAsync(request, cancellationToken);
            }

            ResolvedServicePartition partition = null;
            HttpServiceUriBuilder uriBuilder = new HttpServiceUriBuilder(request.RequestUri);
            NeedsResolveServiceEndpointException exception = null;
            int retries = this.maxRetries;
            int retryDelay = this.initialRetryDelayMs;
            while (retries-- > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                partition = partition != null ? await this.servicePartitionResolver.ResolveAsync(partition, cancellationToken)
                                              : await this.servicePartitionResolver.ResolveAsync(uriBuilder.ServiceName, uriBuilder.PartitionKey, cancellationToken);
                string serviceEndpointJson;
                switch (uriBuilder.Target)
                {
                    case ServiceTarget.Primary:
                        serviceEndpointJson = partition.GetEndpoint().Address;
                        break;
                    case ServiceTarget.Secondary:
                        serviceEndpointJson = this.GetRandomEndpointAddress(partition.Endpoints, 1);
                        break;
                    case ServiceTarget.Any:
                    default:
                        serviceEndpointJson = this.GetRandomEndpointAddress(partition.Endpoints, 0);
                        break;
                }
                string endpointUrl = JObject.Parse(serviceEndpointJson)["Endpoints"][uriBuilder.EndpointName].Value<string>();
                request.RequestUri = new Uri($"{endpointUrl.TrimEnd('/')}/{uriBuilder.ServicePathAndQuery.TrimStart('/')}", UriKind.Absolute);
                
                try
                {
                    HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                    return response;
                }
                catch (NeedsResolveServiceEndpointException ex)
                {
                    exception = ex;
                    if (retries == 0)
                    {
                        break;
                    }
                }

                await Task.Delay(retryDelay);
                retryDelay += retryDelay;
            }

            if (exception.Response != null)
            {
                return exception.Response;
            }
            else
            {
                throw exception.InnerException;
            }
        }
        #endregion

        #region private members
        private string GetRandomEndpointAddress(IEnumerable<ResolvedServiceEndpoint> endpoints, int startIndex)
        {
            var nonEmptyEndpoints = endpoints.Skip(startIndex).Where(e => !string.IsNullOrWhiteSpace(e.Address));
            if (nonEmptyEndpoints.Any())
            {
                return nonEmptyEndpoints.ElementAt(this.random.Next(0, nonEmptyEndpoints.Count())).Address;
            }

            return string.Empty;
        }

        private readonly int maxRetries;
        private readonly int initialRetryDelayMs;
        private readonly Random random = new Random();
        private readonly ServicePartitionResolver servicePartitionResolver = ServicePartitionResolver.GetDefault();
        #endregion
    }
}
