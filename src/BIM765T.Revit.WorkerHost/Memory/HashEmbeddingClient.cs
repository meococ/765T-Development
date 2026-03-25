using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.WorkerHost.Memory;

/// <summary>
/// NON-PRODUCTION: Produces lexical-hash-based embedding vectors, NOT semantic vectors.
/// Two semantically similar sentences with different vocabulary will produce unrelated vectors.
/// This is a fallback when no real embedding service (OpenAI, Azure, local model) is configured.
/// Uses a stable FNV-1a hash over UTF-8 bytes so bucket assignment stays deterministic across
/// platforms and process architectures.
/// </summary>
internal sealed class HashEmbeddingClient : IEmbeddingClient, IEmbeddingProvider
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;
    private readonly int _dimensions;

    public HashEmbeddingClient(int dimensions)
    {
        _dimensions = Math.Max(8, dimensions);
    }

    public string ProviderId => "hash_lexical_fallback";

    public bool IsSemantic => false;

    /// <summary>
    /// Generates a pseudo-embedding by hashing individual tokens and building a term-frequency vector.
    /// This is NOT semantic similarity - semantically similar queries with different words score poorly.
    /// </summary>
    public Task<EmbeddingVector> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var values = new float[_dimensions];
        var tokens = Regex.Split(text ?? string.Empty, @"\W+")
            .Where(x => x.Length > 1)
            .ToArray();
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = ComputeStableBucketIndex(token);
            values[index] += 1f;
        }

        var length = MathF.Sqrt(values.Sum(x => x * x));
        if (length > 0f)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] /= length;
            }
        }

        return Task.FromResult(new EmbeddingVector { Values = values });
    }

    private int ComputeStableBucketIndex(string token)
    {
        var normalized = token.ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = FnvOffsetBasis;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        return (int)(hash % (uint)_dimensions);
    }
}
