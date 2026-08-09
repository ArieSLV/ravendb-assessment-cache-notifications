using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.OngoingTasks;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Raven.Client.Util;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Replication
{
    /// <summary>
    /// Operation to create a pull replication task as a hub.
    /// A pull replication hub allows data to be pulled by sink nodes in other clusters or databases.
    /// Additionally, it can be configured to receive data from sink nodes.
    /// </summary>
    public sealed class PutPullReplicationAsHubOperation : IMaintenanceOperation<ModifyOngoingTaskResult>
    {
        private readonly PullReplicationDefinition _pullReplicationDefinition;

        /// <inheritdoc cref="PutPullReplicationAsHubOperation"/>
        /// <param name="name">The name of the pull replication hub task.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
        public PutPullReplicationAsHubOperation(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"'{nameof(name)}' must have value");
            }

            _pullReplicationDefinition = new PullReplicationDefinition(name);
        }

        /// <inheritdoc cref="PutPullReplicationAsHubOperation"/>
        /// <param name="pullReplicationDefinition">The pull replication hub definition to apply.</param>
        /// <exception cref="ArgumentException">Thrown if the <see cref="PullReplicationDefinition.Name"/> is null or empty.</exception>
        public PutPullReplicationAsHubOperation(PullReplicationDefinition pullReplicationDefinition)
        {
            if (string.IsNullOrEmpty(pullReplicationDefinition.Name))
            {
                throw new ArgumentException($"'{nameof(pullReplicationDefinition.Name)}' must have value");
            }
            _pullReplicationDefinition = pullReplicationDefinition;
        }

        public RavenCommand<ModifyOngoingTaskResult> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new UpdatePullReplicationDefinitionCommand(conventions, _pullReplicationDefinition);
        }

        private sealed class UpdatePullReplicationDefinitionCommand : RavenCommand<ModifyOngoingTaskResult>, IRaftCommand
        {
            private readonly DocumentConventions _conventions;
            private readonly PullReplicationDefinition _pullReplicationDefinition;

            public UpdatePullReplicationDefinitionCommand(DocumentConventions conventions, PullReplicationDefinition pullReplicationDefinition)
            {
                _conventions = conventions;
                _pullReplicationDefinition = pullReplicationDefinition;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/tasks/pull-replication/hub";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    Content = new BlittableJsonContent(async stream => await ctx.WriteAsync(stream, ctx.ReadObject(_pullReplicationDefinition.ToJson(), "update-pull-replication-definition")).ConfigureAwait(false), _conventions)
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ModifyOngoingTaskResult(response);
            }

            public override bool IsReadRequest => false;
            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }
    }
}
