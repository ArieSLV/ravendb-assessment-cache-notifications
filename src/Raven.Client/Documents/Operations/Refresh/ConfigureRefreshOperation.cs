using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Raven.Client.Util;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Refresh
{
    /// <summary>
    /// Operation to configure the refresh task.
    /// By enabling and configuring refresh task, the database automatically refreshes documents
    /// at a specified interval, based on the provided configuration parameters.
    /// </summary>
    public sealed class ConfigureRefreshOperation : IMaintenanceOperation<ConfigureRefreshOperationResult>
    {
        private readonly RefreshConfiguration _configuration;

        /// <inheritdoc cref="ConfigureRefreshOperation"/>
        /// <param name="configuration">
        /// The <see cref="RefreshConfiguration"/> object containing the refresh settings to apply.
        /// This configuration includes:
        /// <list type="bullet">
        /// <item><description>The interval time for refreshing documents (<see cref="RefreshConfiguration.RefreshFrequencyInSec"/>).</description></item>
        /// <item><description>The maximum number of documents to process during each refresh cycle (<see cref="RefreshConfiguration.MaxItemsToProcess"/>).</description></item>
        /// <item><description>A flag indicating whether the refresh operation is enabled or disabled.</description></item>
        /// </list>
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="configuration"/> is null.</exception>
        public ConfigureRefreshOperation(RefreshConfiguration configuration)
        {
            _configuration = configuration;
        }

        public RavenCommand<ConfigureRefreshOperationResult> GetCommand(DocumentConventions conventions, JsonOperationContext ctx)
        {
            return new ConfigureRefreshCommand(conventions, _configuration);
        }

        private sealed class ConfigureRefreshCommand : RavenCommand<ConfigureRefreshOperationResult>, IRaftCommand
        {
            private readonly DocumentConventions _conventions;
            private readonly RefreshConfiguration _configuration;

            public ConfigureRefreshCommand(DocumentConventions conventions, RefreshConfiguration configuration)
            {
                _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            }

            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/refresh/config";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(async stream => await ctx.WriteAsync(stream, DocumentConventions.Default.Serialization.DefaultConverter.ToBlittable(_configuration, ctx)).ConfigureAwait(false), _conventions)
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ConfigureRefreshOperationResult(response);
            }

            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }
    }
}
