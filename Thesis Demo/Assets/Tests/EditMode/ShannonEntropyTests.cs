using NUnit.Framework;

/// <summary>
/// Pure-function tests for the Shannon-entropy helper used by
/// <see cref="BehavioralMetricsCollector.ComputeGoalEntropy"/>.
///
/// Entropy H = -Σ p_i log₂(p_i) for bucket probabilities p_i.
/// Sanity: H = 0 for a single bucket (no diversity);
/// H = log₂(N) for uniform distribution over N buckets (max diversity).
/// </summary>
[TestFixture]
public class ShannonEntropyTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void Empty_ReturnsZero()
    {
        Assert.AreEqual(0f, BehavioralMetricsCollector.ShannonEntropy(new long[0]));
    }

    [Test]
    public void AllZero_ReturnsZero()
    {
        Assert.AreEqual(0f, BehavioralMetricsCollector.ShannonEntropy(new long[] { 0, 0, 0, 0 }));
    }

    [Test]
    public void SingleBucket_ReturnsZero()
    {
        // Everything in one bucket → p = 1, log2(1) = 0, H = 0.
        Assert.AreEqual(0f, BehavioralMetricsCollector.ShannonEntropy(new long[] { 100, 0, 0, 0 }), Tolerance);
    }

    [Test]
    public void TwoEqualBuckets_ReturnsOneBit()
    {
        // Uniform over 2 buckets → H = log₂(2) = 1.
        Assert.AreEqual(1f, BehavioralMetricsCollector.ShannonEntropy(new long[] { 50, 50 }), Tolerance);
    }

    [Test]
    public void FourEqualBuckets_ReturnsTwoBits()
    {
        // Uniform over 4 buckets → H = log₂(4) = 2.
        Assert.AreEqual(2f, BehavioralMetricsCollector.ShannonEntropy(new long[] { 25, 25, 25, 25 }), Tolerance);
    }

    [Test]
    public void NineEqualBuckets_ReturnsLog2Nine()
    {
        // 9 goal types uniform → H ≈ 3.1699 bits. This is the upper bound for the
        // collector's 9-bucket goal distribution.
        float expected = (float)System.Math.Log(9, 2);
        Assert.AreEqual(expected, BehavioralMetricsCollector.ShannonEntropy(
            new long[] { 10, 10, 10, 10, 10, 10, 10, 10, 10 }), Tolerance);
    }

    [Test]
    public void SkewedDistribution_ReturnsHandComputed()
    {
        // {75, 25} → p = 0.75, 0.25.
        // H = -(0.75 * log2(0.75) + 0.25 * log2(0.25))
        //   = -(0.75 * -0.4150 + 0.25 * -2)
        //   = -(-0.3113 + -0.5)
        //   = 0.8113
        float expected = 0.8112781f;
        Assert.AreEqual(expected, BehavioralMetricsCollector.ShannonEntropy(new long[] { 75, 25 }), Tolerance);
    }

    [Test]
    public void MoreDiverse_HasHigherEntropy()
    {
        float skewed = BehavioralMetricsCollector.ShannonEntropy(new long[] { 90, 10 });
        float balanced = BehavioralMetricsCollector.ShannonEntropy(new long[] { 50, 50 });
        Assert.Less(skewed, balanced,
            "A balanced distribution must have higher entropy than a skewed one.");
    }
}
