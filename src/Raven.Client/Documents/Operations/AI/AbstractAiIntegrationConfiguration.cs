using Newtonsoft.Json;
using Raven.Client.Documents.Operations.ETL;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.AI;
public abstract class AbstractAiIntegrationConfiguration : EtlConfiguration<AiConnectionString>
{
    [JsonDeserializationIgnore]
    [JsonIgnore]
    public AiConnectorType AiConnectorType => Connection?.GetActiveProvider() ?? AiConnectorType.None;
}
