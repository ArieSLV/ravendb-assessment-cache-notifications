using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents.Operations.AI;
using Raven.Server.Documents.ETL.Providers.AI;
using Raven.Server.Documents.ETL.Providers.AI.Extensions;
using Microsoft.SemanticKernel.Services;

namespace Raven.Server.Documents.AI;

public static class AiHelper
{
    [Experimental("SKEXP0001")]
    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingService(AiConnectionString connectionString)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Configure(connectionString, withLogging: false);
        var kernel = kernelBuilder.Build();
        return kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    [Experimental("SKEXP0001")]
    public static (IEmbeddingGenerator<string, Embedding<float>>, InMemoryLoggerProvider) CreateEmbeddingServicesForTest(EmbeddingsGenerationConfiguration configuration)
        => CreateAiServicesForTest<IEmbeddingGenerator<string, Embedding<float>>,EmbeddingsGenerationConfiguration>(configuration);

    private static (TService, InMemoryLoggerProvider) CreateAiServicesForTest<TService, TConfig>(TConfig configuration)
        where TConfig : AbstractAiIntegrationConfiguration
        where TService : class, IEmbeddingGenerator<string, Embedding<float>>
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Configure(configuration, withLogging: true);
        var kernel = kernelBuilder.Build();

        var llmService = kernel.GetRequiredService<TService>();
        var logger = (InMemoryLoggerProvider)kernel.GetRequiredService<ILoggerProvider>();

        return (llmService, logger);
    }
}
