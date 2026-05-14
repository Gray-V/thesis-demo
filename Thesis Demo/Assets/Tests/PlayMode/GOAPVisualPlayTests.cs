using System.Collections;
using System.Collections.Generic;
using CrashKonijn.Goap.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Visual PlayMode tests — one per GOAP goal for GOAPWithBOIDSMovement.
/// These run at Time.timeScale = 1 so you can watch them in the Game view while
/// they execute. Each test builds a scenario that should force one specific goal,
/// yields for 5 seconds of real time, then asserts the goal fired.
///
/// SETUP (one-time, in Unity Editor):
///   1. File → Build Profiles → Scene List → add "Assets/Scenes/New Scene.unity"
///   2. Open Window → General → Test Runner → PlayMode tab
///   3. Click any test's Run button. The test loads the scene, sets up the
///      scenario, runs 5s at real time, and asserts.
///
/// If the test fails at SetUp with an "add to Build Settings" message, step 1
/// above hasn't been done yet.
/// </summary>
[TestFixture]
public class GOAPVisualPlayTests
{
    private const string SceneName = "New Scene";
    private const float ObservationSeconds = 5f;
    private const int Seed = 42;

    private GameObject prefab;
    private Transform playerTransform;
    private GoapBehaviour goapBehaviour;
    private readonly List<GOAPBoidAgent> spawned = new List<GOAPBoidAgent>();
    private readonly List<GameObject> testOwned = new List<GameObject>();
    private GOAPBoidFlockManager flockManager;

    // ── Fixture lifecycle ──────────────────────────────────────────

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Real-time playback so the Game view shows what's happening.
        Time.timeScale = 1f;
        Random.InitState(Seed);

        // Load the production scene so GoapBehaviour + agent-type refs + Player + prefab are all available.
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. " +
                "Open File → Build Profiles → Scene List and add 'Assets/Scenes/New Scene.unity'.");
            yield break;
        }

        var loadOp = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!loadOp.isDone) yield return null;
        yield return null; // let Start() fire on scene-level components

        // Clear whatever ConditionManager spawned so our scenario starts clean.
        var cm = Object.FindFirstObjectByType<ConditionManager>();
        if (cm == null)
            Assert.Inconclusive($"No ConditionManager in scene '{SceneName}'.");

        cm.ClearAgents();
        prefab = cm.GOAPWithBOIDSMovementPrefab;
        if (prefab == null)
            Assert.Inconclusive("ConditionManager.goapWithBOIDSMovementPrefab is not assigned.");

        // GoapBehaviour is registered in-scene and holds the agent-type map.
        goapBehaviour = Object.FindFirstObjectByType<GoapBehaviour>();
        if (goapBehaviour == null)
            Assert.Inconclusive("No GoapBehaviour in scene — GOAP planning won't run.");

        // Player must be tagged "Player".
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO == null)
            Assert.Inconclusive("No GameObject tagged 'Player' in scene.");
        playerTransform = playerGO.transform;

        // Disable AutomatedPlayer — it homes on enemies and would override the
        // scenario's scripted player position, breaking Wander/Kite/Guard/Regroup.
        var ap = playerGO.GetComponent<AutomatedPlayer>();
        if (ap != null) ap.enabled = false;

        // Zero any residual velocity on a rigidbody player so it stays put.
        var rb = playerGO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Let the scene settle one frame after ClearAgents — Destroy is deferred
        // to end-of-frame, so AllAgents still contains the cleared entries until
        // their OnDisable fires next frame.
        yield return null;

        // Re-seed after all scene Starts have consumed Random.
        Random.InitState(Seed);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (var go in testOwned)
            if (go != null) Object.Destroy(go);
        testOwned.Clear();
        spawned.Clear();
        flockManager = null;

        GOAPBoidAgent.ResetAttackSlots();
        yield return null;
    }

    // ── Scenarios ──────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Visual_Wander_PlayerFar_AgentsShouldWander()
    {
        MovePlayerTo(50f, 0f, 0f);
        SpawnFlock(count: 3, ranged: false);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Wander, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Attack_MeleeAgentsNearPlayer()
    {
        MovePlayerTo(8f, 0f, 0f);
        SpawnFlock(count: 3, ranged: false);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Attack, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_RangedAttack_RangedAgentsInFiringRange()
    {
        MovePlayerTo(12f, 0f, 0f);
        SpawnFlock(count: 3, ranged: true);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.RangedAttack, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Flee_FlockHPDrainedTo25Percent()
    {
        MovePlayerTo(8f, 0f, 0f);
        SpawnFlock(count: 5, ranged: false);
        DrainFlockHealthTo(0.25f);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Flee, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Scatter_FlockHPBelowCritical()
    {
        MovePlayerTo(8f, 0f, 0f);
        SpawnFlock(count: 6, ranged: false);
        DrainFlockHealthTo(0.1f);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Scatter, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Kite_RangedAgentTooCloseToPlayer()
    {
        MovePlayerTo(5f, 0f, 0f);
        SpawnFlock(count: 3, ranged: true);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Kite, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Regroup_IsolatedAgentFarFromFlockmates()
    {
        // Player far so playerNearby=false; Attack/Kite/Flee path skipped.
        MovePlayerTo(60f, 0f, 0f);
        SpawnFlock(count: 3, ranged: false);
        // Yank one agent 40u away and flag it scattering so cohesion/leash won't
        // pull it back before the brain observes isolation > 20u from centroid.
        if (spawned.Count > 0)
        {
            spawned[0].transform.position = new Vector3(0f, 0f, 40f);
            spawned[0].isScattering = true;
            spawned[0].SetVelocity(Vector3.zero);
        }
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Regroup, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Flank_AttackSlotsSaturated()
    {
        MovePlayerTo(10f, 0f, 0f);
        SpawnFlock(count: 5, ranged: false);
        // Saturate the static attack-slot pool (default max 3 per flock).
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Flank, ObservationSeconds);
    }

    [UnityTest]
    public IEnumerator Visual_Guard_PlayerInGuardRing()
    {
        // Player 22u away → inside [guardInnerRange=15, guardOuterRange=30].
        // Keep flock count < 3 so Flank doesn't win. Also force cooldown-not-ready
        // so Attack doesn't win (Attack requires cooldown ready + slot available).
        MovePlayerTo(22f, 0f, 0f);
        SpawnFlock(count: 2, ranged: false);
        foreach (var a in spawned)
            if (a != null) a.cooldownTimer = 999f;
        yield return ObserveAndAssertGoal(GoalPriorityResolver.GoalType.Guard, ObservationSeconds);
    }

    // ── Scenario helpers ───────────────────────────────────────────

    private void MovePlayerTo(float x, float y, float z)
    {
        playerTransform.position = new Vector3(x, y, z);
    }

    private void SpawnFlock(int count, bool ranged)
    {
        var mgrGO = new GameObject($"VisualTest_Flock_{(ranged ? "Ranged" : "Melee")}");
        testOwned.Add(mgrGO);
        mgrGO.transform.position = Vector3.zero;
        flockManager = mgrGO.AddComponent<GOAPBoidFlockManager>();
        flockManager.Configure(ranged ? FlockType.Ranged : FlockType.Melee, 0, count);

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * 3f, 0f, Mathf.Sin(angle) * 3f);

            GameObject go = Object.Instantiate(prefab, pos, Quaternion.identity);
            go.name = $"VisualTest_{(ranged ? "Ranged" : "Melee")}_{i}";
            testOwned.Add(go);

            var agent = go.GetComponent<GOAPBoidAgent>();
            if (agent != null)
            {
                agent.SetAttackType(ranged ? AttackType.Ranged : AttackType.Melee);
                agent.flockId = 0;
                flockManager.RegisterAgent(agent);
                spawned.Add(agent);
            }

            var provider = go.GetComponent<GoapActionProvider>();
            if (provider != null)
                provider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");
        }
    }

    private void DrainFlockHealthTo(float targetFraction)
    {
        if (flockManager == null) return;
        int safety = 200;
        while (flockManager.HealthPercent > targetFraction && safety-- > 0)
        {
            float before = flockManager.HealthPercent;
            flockManager.TakeDamage(50f);
            if (flockManager.HealthPercent >= before) break;
        }
    }

    /// <summary>
    /// Observe agents for <paramref name="seconds"/>, sampling every frame, and
    /// pass the moment any agent's resolved goal matches <paramref name="expected"/>.
    ///
    /// This pattern (vs a single sample at T=end) is necessary because
    /// <c>GOAPBoidBrain.currentGoalType</c> is the DESIRED next goal, not the
    /// running action. Once an agent grabs an attack slot the desired goal flips
    /// to Flank even while the attack is still running, so the expected goal can
    /// appear briefly then get overwritten.
    /// </summary>
    private IEnumerator ObserveAndAssertGoal(GoalPriorityResolver.GoalType expected, float seconds)
    {
        var seen = new HashSet<GoalPriorityResolver.GoalType>();
        bool found = false;
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                var a = spawned[i];
                if (a == null) continue;
                var brain = a.GetComponent<GOAPBoidBrain>();
                if (brain == null) continue;
                seen.Add(brain.currentGoalType);
                if (brain.currentGoalType == expected) found = true;
            }
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        if (!found)
        {
            Assert.Fail(
                $"Never saw goal {expected} during {seconds:F1}s of observation. " +
                $"Observed: {{{string.Join(", ", seen)}}}. " +
                $"Check GoalPriorityResolver trigger conditions.");
        }
    }
}
