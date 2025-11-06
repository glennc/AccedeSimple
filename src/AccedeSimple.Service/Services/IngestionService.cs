using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;

namespace AccedeSimple.Service.Services;

public class IngestionService(
    VectorStore vectorStore,
    ILoggerFactory loggerFactory,
    ILogger<IngestionService> logger,
    PdfPigReader reader,
    Tokenizer tokenizer,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    public async Task IngestAsync(string sourceDirectory)
    {
        // Create semantic chunker - groups content by topic similarity
        var chunkerOptions = new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = 512,
            OverlapTokens = 50        // Small overlap to preserve context at boundaries
        };
        var chunker = new SemanticSimilarityChunker(
            embeddingGenerator,
            chunkerOptions);

        // Create writer. dimensionCount must match the model that is being used to generate embeddings.
        using var writer = new VectorStoreWriter<string>(vectorStore, dimensionCount: EmbeddingModel.DIMENSION);

        // Create and configure the pipeline
        using var pipeline = new IngestionPipeline<string>(reader, chunker, writer, loggerFactory: loggerFactory);

        // Process all PDF files in the directory
        await foreach( var result in pipeline.ProcessAsync(new DirectoryInfo(sourceDirectory), "*.pdf"))
        {
            if (result.Succeeded)
            {
                //TODO: There is a PR out that would change this to result.DocumentId instead.
                logger.LogInformation("Ingested document: {Document}", result.Document?.Identifier);
            }
            else
            {
                logger.LogError("Failed to ingest document: {Error}", result.Exception);
            }
        }
    }
}