using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Raven.Client.Util;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.TimeSeries
{
    /// <summary>
    /// Operation to configure value names for a time series in a specific collection.
    /// </summary>
    public sealed class ConfigureTimeSeriesValueNamesOperation : IMaintenanceOperation<ConfigureTimeSeriesOperationResult>
    {
        private readonly Parameters _parameters;

        /// <inheritdoc cref="ConfigureTimeSeriesValueNamesOperation"/>
        /// <param name="parameters">
        /// The parameters for configuring value names, including the collection name, time series name, 
        /// and the new value names to set.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="parameters"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown if the provided parameters are invalid (e.g., missing required fields).
        /// </exception>
        public ConfigureTimeSeriesValueNamesOperation(Parameters parameters)
        {
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _parameters.Validate();
        }

        public RavenCommand<ConfigureTimeSeriesOperationResult> GetCommand(DocumentConventions conventions, JsonOperationContext ctx)
        {
            return new ConfigureTimeSeriesValueNamesCommand(conventions, _parameters);
        }

        private sealed class ConfigureTimeSeriesValueNamesCommand : RavenCommand<ConfigureTimeSeriesOperationResult>, IRaftCommand
        {
            private readonly DocumentConventions _conventions;
            private readonly Parameters _parameters;

            public ConfigureTimeSeriesValueNamesCommand(DocumentConventions conventions, Parameters parameters)
            {
                _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
                _parameters = parameters;
            }

            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/timeseries/names/config";
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(async stream =>
                    {
                        var config = ctx.ReadObject(_parameters.ToJson(), "convert time-series configuration");
                        await ctx.WriteAsync(stream, config).ConfigureAwait(false);
                    }, _conventions)
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ConfigureTimeSeriesOperationResult(response);
            }

            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }

        /// <summary>
        /// Encapsulates the parameters needed for configuring value names for a time series in a specific collection.
        /// </summary>
        public sealed class Parameters : IDynamicJson
        {
            /// <summary>
            /// The name of the collection where the time series resides.
            /// </summary>
            public string Collection;

            /// <summary>
            /// The name of the time series to configure.
            /// </summary>
            public string TimeSeries;

            /// <summary>
            /// The list of value names to associate with the time series.
            /// </summary>
            public string[] ValueNames;

            /// <summary>
            /// Indicates whether to update an existing configuration for the specified time series in the collection.
            /// If set to <c>false</c> and a configuration already exists for this time series in the collection, 
            /// an exception will be thrown.
            /// </summary>
            public bool Update;

            internal void Validate()
            {
                if (string.IsNullOrEmpty(Collection))
                    throw new ArgumentNullException(nameof(Collection));
                if (string.IsNullOrEmpty(TimeSeries))
                    throw new ArgumentNullException(nameof(TimeSeries));
                if (ValueNames == null || ValueNames.Length == 0)
                    throw new ArgumentException($"{nameof(ValueNames)} can't be empty.");
            }

            public DynamicJsonValue ToJson()
            {
                return new DynamicJsonValue
                {
                    [nameof(Collection)] = Collection,
                    [nameof(TimeSeries)] = TimeSeries,
                    [nameof(ValueNames)] = new DynamicJsonArray(ValueNames),
                    [nameof(Update)] = Update
                };
            }
        }
    }
}
