using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asset-level parity check: every BoidSettings ScriptableObject in the project
/// must have a non-zero obstacleMask. BoidAgent.ComputeObstacleAvoidance early-returns
/// when the mask is zero, which silently disables avoidance and lets boids phase
/// through environment geometry. This was the root cause of Bug 2 (BOIDS pass through
/// objects). Failing this test means the same regression has crept back in.
/// </summary>
[TestFixture]
public class BoidSettingsAssetTests
{
    [Test]
    public void AllBoidSettingsAssets_HaveNonZeroObstacleMask()
    {
        string[] guids = AssetDatabase.FindAssets("t:BoidSettings");
        Assert.Greater(guids.Length, 0, "Expected at least one BoidSettings asset in the project");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadAssetAtPath<BoidSettings>(path);
            Assert.IsNotNull(settings, $"Failed to load BoidSettings at {path}");
            Assert.AreNotEqual(0, settings.obstacleMask.value,
                $"BoidSettings '{path}' has obstacleMask=0 — obstacle avoidance is disabled and boids will phase through geometry. Set the mask to your environment-collider layer.");
        }
    }
}
