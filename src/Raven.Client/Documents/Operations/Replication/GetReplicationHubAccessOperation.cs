using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json.Serialization;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Replication
{
    /// <summary>
    /// Operation to retrieve access details for a specific replication hub task.
    /// This operation provides a paginated list of detailed access information, including the nodes or sinks
    /// that are authorized to access the specified replication hub.
    /// </summary>
    public sealed class GetReplicationHubAccessOperation : IMaintenanceOperation<DetailedReplicationHubAccess[]>
    {
        private readonly string _hubName;
        private readonly int _start;
        private readonly int _pageSize;

        /// <inheritdoc cref="GetReplicationHubAccessOperation"/>
        /// <param name="hubName">The name of the replication hub task for which access details are retrieved.</param>
        /// <param name="start">The starting point for pagination (default is 0).</param>
        /// <param name="pageSize">The maximum number of records to return per page (default is 25).</param>
        public GetReplicationHubAccessOperation(string hubName, int start = 0, int pageSize = 25)
        {
            _hubName = hubName;
            _start = start;
            _pageSize = pageSize;
        }

        public RavenCommand<DetailedReplicationHubAccess[]> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new GetReplicationHubAccessCommand(_hubName, _start, _pageSize);
        }

        private sealed class GetReplicationHubAccessCommand : RavenCommand<DetailedReplicationHubAccess[]>
        {
            private readonly string _hubName;
            private readonly int _start;
            private readonly int _pageSize;

            public GetReplicationHubAccessCommand(string hubName, int start, int pageSize)
            {
                if (string.IsNullOrWhiteSpace(hubName))
                    throw new ArgumentException("Value cannot be null or whitespace.", nameof(hubName));
                _hubName = hubName;
                _start = start;
                _pageSize = pageSize;
            }

            public override bool IsReadRequest { get; } = true;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/tasks/pull-replication/hub/access?name={Uri.EscapeDataString(_hubName)}&start={_start}&pageSize={_pageSize}";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ReplicationHubAccessResult(response).Results;
            }
        }
    }
}
