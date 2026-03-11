using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace IoT_AI_Demo.Orchestrator;

/// <summary>
/// Deterministic mock embedding generator for local development without Azure OpenAI.
/// Produces normalized 1536-dimension vectors derived from a SHA-256 hash of the input text,
/// so identical inputs always produce identical embeddings.
/// </summary>
public sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 1536;

    public EmbeddingGeneratorMetadata Metadata { get; } =
        new("mock-embedding");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var text in values)
            embeddings.Add(new Embedding<float>(CreateVector(text)));
        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private static float[] CreateVector(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var seed = BitConverter.ToInt32(hash, 0);
        var rng = new Random(seed);

        var vector = new float[Dimensions];
        float norm = 0;
        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(rng.NextDouble() * 2 - 1);
            norm += vector[i] * vector[i];
        }
        norm = MathF.Sqrt(norm);
        for (int i = 0; i < Dimensions; i++)
            vector[i] /= norm;

        return vector;
    }
}
