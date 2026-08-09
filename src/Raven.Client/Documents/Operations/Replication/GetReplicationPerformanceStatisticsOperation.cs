using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Replication;
using Raven.Client.Http;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Replication
{
    /// <summary>
    /// Operation to retrieve replication performance statistics.
    /// This operation provides detailed metrics and performance data about the replication processes,
    /// which can help diagnose issues and monitor the replication behavior.
    /// </summary>
    public sealed class GetReplicationPerformanceStatisticsOperation : IMaintenanceOperation<ReplicationPerformance>
    {
        public RavenCommand<ReplicationPerformance> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new GetReplicationPerformanceStatisticsCommand();
        }

        internal sealed class GetReplicationPerformanceStatisticsCommand : RavenCommand<ReplicationPerformance>
        {
            public GetReplicationPerformanceStatisticsCommand()
            {
            }

            public GetReplicationPerformanceStatisticsCommand(string nodeTag)
            {
                SelectedNodeTag = nodeTag;
            }
            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/replication/performance";

                return new HttpRequestMessage
                {
                    Method = HttpMethod.Get
                };
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = DocumentConventions.Default.Serialization.DefaultConverter.FromBlittable<ReplicationPerformance>(response, "replication/performance");
            }
        }
    }
}
