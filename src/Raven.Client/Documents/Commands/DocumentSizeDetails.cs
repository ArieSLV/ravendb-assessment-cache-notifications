using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Commands;

internal sealed class DocumentSizeDetails : SizeDetails
{
    public string DocId { get; set; }

    public override DynamicJsonValue ToJson()
    {
        var json = base.ToJson();
        json[nameof(DocId)] = DocId;
        return json;
    }
}
