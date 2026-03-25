using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Memory;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class HashEmbeddingClientTests
{
    [Fact]
    public async Task EmbedAsync_UsesStableCaseInsensitiveFnvBuckets()
    {
        const int dimensions = 16;
        var client = new HashEmbeddingClient(dimensions);

        var mixedCase = await client.EmbedAsync("Door PIPE wall door", CancellationToken.None);
        var normalizedCase = await client.EmbedAsync("door pipe WALL DOOR", CancellationToken.None);

        Assert.Equal(mixedCase.Values, normalizedCase.Values);

        var expected = new float[dimensions];
        Increment(expected, "door", dimensions);
        Increment(expected, "pipe", dimensions);
        Increment(expected, "wall", dimensions);
        Increment(expected, "door", dimensions);
        Normalize(expected);

        Assert.Equal(expected.Length, mixedCase.Values.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], mixedCase.Values[index], 6);
        }
    }

    [Fact]
    public async Task EmbedAsync_NormalizesVectorLength()
    {
        var client = new HashEmbeddingClient(8);
        var vector = await client.EmbedAsync("alpha beta alpha gamma", CancellationToken.None);

        var length = MathF.Sqrt(vector.Values.Sum(x => x * x));
        Assert.InRange(length, 0.9999f, 1.0001f);
    }

    private static void Increment(float[] values, string token, int dimensions)
    {
        var index = ComputeStableBucketIndex(token, dimensions);
        values[index] += 1f;
    }

    private static void Normalize(float[] values)
    {
        var length = MathF.Sqrt(values.Sum(x => x * x));
        if (length <= 0f)
        {
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] /= length;
        }
    }

    private static int ComputeStableBucketIndex(string token, int dimensions)
    {
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;

        var normalized = token.ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = fnvOffsetBasis;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= fnvPrime;
        }

        return (int)(hash % (uint)dimensions);
    }
}
