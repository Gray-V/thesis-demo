using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Visual PlayMode tests for the PureBOIDS (condition 2) flock-level state machine,
/// mirroring GOAPVisualPlayTests. Each test forces condition 2, sets up a stimulus,
/// and observes <see cref="FlockManager.State"/> every frame over 5 seconds — the
/// expected state has to appear at least once to pass.
///
/// SETUP (one-time, in Unity Editor):
///   1. File → Build Profiles → Scene List → add "Assets/Scenes/New Scene.unity"
///   2. Open Window → General → Test Runner → PlayMode tab → run any test.
/// </summary>
[TestFixture]
public class PureBoidsVisualPlayTests
{
    private const string SceneName = "New Scene";
    private const float ObservationSeconds = 5f;
    private const int Seed = 42;

    private Transform playerTransform;
    private FlockManager meleeFlock;
    private FlockManager rangedFlock;

    // ── Fixture lifecycle ──────────────────────────────────────────

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Time.timeScale = 1f;
        Random.InitState(Seed);

        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. " +
                "Open File → Build Profiles → Scene List and add 'Assets/Scenes/New Scene.unity'.");
            yield break;
        }

        var loadOp = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!loadOp.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        if (cm == null)
            Assert.Inconclusive($"No ConditionManager in scene '{SceneName}'.");

        // Force condition 2 and respawn — the default scene condition may differ.
        cm.ClearAgents();
        cm.CurrentCondition = AgentCondition.PureBOIDS;
        cm.SpawnAgentsForCondition();

        // FlockManager.Start() runs SpawnFlock on the frame after Instantiate. Wait two.
        yield return null;
        yield return null;

        var flocks = Object.FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        foreach (var f in flocks)
        {
            if (f.FlockType == FlockType.Melee) meleeFlock = f;
            else if (f.FlockType == FlockType.Ranged) rangedFlock = f;
        }

        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO == null)
            Assert.Inconclusive("No GameObject tagged 'Player' in scene.");
        playerTransform = playerGO.transform;

        var ap = playerGO.GetComponent<AutomatedPlayer>();
        if (ap != null) ap.enabled = false;

        var rb = playerGO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Random.InitState(Seed);
    }

    // ── Scenarios ──────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Visual_Idle_NoTarget_FlockDrifts()
    {
        // No target assigned — FlockManager stays Idle (or transitions out via scatter timer etc.)
        MovePlayerTo(80f, 0f, 0f); // far away so aggro trigger doesn't fire
        yield return ObserveAndAssertState(FlockManager.FlockState.Idle, meleeFlock);
    }

    [UnityTest]
    public IEnumerator Visual_Flee_MeleeFlockLowHP_Retreats()
    {
        MovePlayerTo(8f, 0f, 0f);
        SetTarget(meleeFlock, playerTransform);
        DrainFlockHPTo(meleeFlock, 0.25f);
        yield return ObserveAndAssertState(FlockManager.FlockState.Fleeing, meleeFlock);
    }

    [UnityTest]
    public IEnumerator Visual_Scatter_MeleeFlockCriticalHP_Disperses()
    {
        MovePlayerTo(8f, 0f, 0f);
        SetTarget(meleeFlock, playerTransform);
        DrainFlockHPTo(meleeFlock, 0.1f);
        yield return ObserveAndAssertState(FlockManager.FlockState.Scattering, meleeFlock);
    }

    [UnityTest]
    public IEnumerator Visual_Kite_RangedFlockPlayerTooClose_Backpedals()
    {
        // Ranged flock with player inside kiteMinDistance (default 8u).
        if (rangedFlock == null) Assert.Inconclusive("No ranged flock spawned.");
        MovePlayerTo(5f, 0f, 0f);
        SetTarget(rangedFlock, playerTransform);
        yield return ObserveAndAssertState(FlockManager.FlockState.Kiting, rangedFlock);
    }

    [UnityTest]
    public IEnumerator Visual_Guard_PlayerAtMidRange_HoldsPerimeter()
    {
        // Player at 22u — inside guard band [15, 30] but outside aggroRadius (15 default).
        MovePlayerTo(22f, 0f, 0f);
        SetTarget(meleeFlock, playerTransform);
        yield return ObserveAndAssertState(FlockManager.FlockState.Guarding, meleeFlock);
    }

    [UnityTest]
    public IEnumerator Visual_Regroup_IsolatedBoid_PullsBack()
    {
        // No target → rule out combat states. Displace one boid far from centroid.
        MovePlayerTo(80f, 0f, 0f);
        if (meleeFlock.Boids.Count == 0) Assert.Inconclusive("Melee flock has no boids.");
        var stray = meleeFlock.Boids[0];
        stray.transform.position = meleeFlock.transform.position + new Vector3(0f, 0f, 30f);
        yield return ObserveAndAssertState(FlockManager.FlockState.Regrouping, meleeFlock);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private void MovePlayerTo(float x, float y, float z)
    {
        playerTransform.position = new Vector3(x, y, z);
    }

    private void SetTarget(FlockManager mgr, Transform t)
    {
        if (mgr == null) return;
        mgr.SetTarget(t);
    }

    private void DrainFlockHPTo(FlockManager mgr, float targetFraction)
    {
        if (mgr == null) return;
        int safety = 500;
        while (mgr.HealthPercent > targetFraction && safety-- > 0)
        {
            float before = mgr.HealthPercent;
            mgr.TakeDamage(50f);
            if (mgr.HealthPercent >= before) break;
        }
    }

    /// <summary>
    /// Observe <paramref name="mgr"/>.State every frame for <see cref="ObservationSeconds"/>.
    /// Pass as soon as the expected state appears. Matches the GOAPVisualPlayTests
    /// observe-any-frame pattern so transient states (e.g. Scattering auto-expires
    /// back to Fleeing after settings.scatterDuration) still register.
    /// </summary>
    private IEnumerator ObserveAndAssertState(FlockManager.FlockState expected, FlockManager mgr)
    {
        if (mgr == null)
        {
            Assert.Inconclusive("FlockManager ref is null — check scene prefab assignments.");
            yield break;
        }

        var seen = new HashSet<FlockManager.FlockState>();
        bool found = false;
        float elapsed = 0f;

        while (elapsed < ObservationSeconds)
        {
            seen.Add(mgr.State);
            if (mgr.State == expected) found = true;
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        if (!found)
        {
            Assert.Fail(
                $"Never saw state {expected} on flock {mgr.FlockType} during {ObservationSeconds:F1}s. " +
                $"Observed: {{{string.Join(", ", seen)}}}. Check FlockStateResolver thresholds.");
        }
    }
}
