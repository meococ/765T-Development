using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class RequestRateLimiter
{
    private const int BucketEvictionThreshold = 512;
    private readonly ConcurrentDictionary<string, SlidingWindowBucket> _buckets = new ConcurrentDictionary<string, SlidingWindowBucket>(StringComparer.OrdinalIgnoreCase);
    private readonly int _standardLimit;
    private readonly int _highRiskLimit;
    private readonly TimeSpan _window;
    private readonly ISystemClock _clock;

    internal RequestRateLimiter(AgentSettings settings, ISystemClock clock)
    {
        _standardLimit = Math.Max(0, settings.MaxRequestsPerMinute);
        _highRiskLimit = Math.Max(0, settings.MaxHighRiskRequestsPerMinute);
        _window = TimeSpan.FromSeconds(Math.Max(10, settings.RequestRateLimitWindowSeconds));
        _clock = clock;
    }

    internal RateLimitDecision Evaluate(string caller, ToolManifest? manifest)
    {
        var isHighRisk = manifest?.ApprovalRequirement == ApprovalRequirement.HighRiskToken;
        var limit = isHighRisk ? _highRiskLimit : _standardLimit;
        if (limit <= 0)
        {
            return RateLimitDecision.Allow();
        }

        var bucketKey = BuildBucketKey(caller, isHighRisk);
        var bucket = _buckets.GetOrAdd(bucketKey, _ => new SlidingWindowBucket());
        var now = _clock.UtcNow;
        var decision = bucket.TryAcquire(limit, _window, now);

        if (_buckets.Count > BucketEvictionThreshold && bucket.IsEmpty(_window, now))
        {
            if (_buckets.TryGetValue(bucketKey, out var currentBucket) && ReferenceEquals(currentBucket, bucket))
            {
                _buckets.TryRemove(bucketKey, out _);
            }
        }

        return decision;
    }

    private static string BuildBucketKey(string caller, bool isHighRisk)
    {
        var normalizedCaller = string.IsNullOrWhiteSpace(caller) ? "<anonymous>" : caller.Trim();
        return normalizedCaller + "|" + (isHighRisk ? "high_risk" : "standard");
    }

    private sealed class SlidingWindowBucket
    {
        private readonly Queue<DateTime> _timestamps = new Queue<DateTime>();
        private readonly object _gate = new object();

        internal RateLimitDecision TryAcquire(int limit, TimeSpan window, DateTime now)
        {
            lock (_gate)
            {
                TrimExpired(window, now);

                if (_timestamps.Count >= limit)
                {
                    var retryAfter = window - (now - _timestamps.Peek());
                    if (retryAfter < TimeSpan.Zero)
                    {
                        retryAfter = TimeSpan.Zero;
                    }

                    return RateLimitDecision.Deny(retryAfter, limit, (int)Math.Round(window.TotalSeconds), _timestamps.Count);
                }

                _timestamps.Enqueue(now);
                return RateLimitDecision.Allow(limit, (int)Math.Round(window.TotalSeconds), _timestamps.Count);
            }
        }

        internal bool IsEmpty(TimeSpan window, DateTime now)
        {
            lock (_gate)
            {
                TrimExpired(window, now);
                return _timestamps.Count == 0;
            }
        }

        private void TrimExpired(TimeSpan window, DateTime now)
        {
            while (_timestamps.Count > 0 && now - _timestamps.Peek() >= window)
            {
                _timestamps.Dequeue();
            }
        }
    }
}

internal sealed class RateLimitDecision
{
    private RateLimitDecision()
    {
    }

    internal bool Allowed { get; private set; }
    internal TimeSpan RetryAfter { get; private set; }
    internal int Limit { get; private set; }
    internal int WindowSeconds { get; private set; }
    internal int CurrentCount { get; private set; }

    internal static RateLimitDecision Allow(int limit = 0, int windowSeconds = 0, int currentCount = 0)
    {
        return new RateLimitDecision
        {
            Allowed = true,
            Limit = limit,
            WindowSeconds = windowSeconds,
            CurrentCount = currentCount
        };
    }

    internal static RateLimitDecision Deny(TimeSpan retryAfter, int limit, int windowSeconds, int currentCount)
    {
        return new RateLimitDecision
        {
            Allowed = false,
            RetryAfter = retryAfter,
            Limit = limit,
            WindowSeconds = windowSeconds,
            CurrentCount = currentCount
        };
    }
}
