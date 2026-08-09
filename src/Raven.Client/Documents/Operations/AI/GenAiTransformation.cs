using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.AI;

public class GenAiTransformation
{
    public string Script { get; set; }

    public bool ValidateScript(out string error)
    {
        error = string.Empty;
        if (Script.Contains("ai.genContext")) 
            return true;
        error = "You must call the ai.genContext(ctx) function in your script";
        return false;

    }

    public DynamicJsonValue ToJson() => new()
    {
        [nameof(Script)] = Script
    };
}
