using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which benchmark mode a trial runs in.
/// Combat: current behaviour — damage on, variable duration, terminates on PlayerDeath /
/// FlockWiped / TimeLimit. Sources RQ2 (behavioural effectiveness) data.
/// Stress: player + flock pooled HP invulnerable for the trial. Live agent count stays
/// at the configured N for the full <see cref="ExperimentRunner.experimentDuration"/>,
/// removing the death-rate confound that contaminates per-trial FPS comparisons. Sources
/// RQ1 (computational scaling) data. See [[methodology-revisions-2026-04]] items 1 / 3b.
/// </summary>
public enum BenchmarkMode { Combat, Stress }

/// <summary>
/// Automated experiment runner for thesis data collection.
///
/// Each invocation runs a **batch** of trials: <see cref="runsPerCondition"/>
/// trials per condition, interleaved so early runs of every condition finish
/// before later ones (avoids losing a condition if the session is stopped early).
///
/// Per-trial seeds are deterministic — trial i of any condition uses seed
/// <c>baseSeed + i</c>, so two batch runs with the same configuration produce
/// identical data for statistical validation.
///
/// A trial ends on the earliest of:
///   - PlayerDeath — <see cref="PlayerHealth.IsDead"/> returns true.
///   - FlockWiped  — <see cref="ConditionManager.AllFlocksDead"/> returns true.
///   - TimeLimit   — <see cref="experimentDuration"/> seconds elapsed.
///
/// Output files (written to <see cref="outputDirectory"/>, one set per batch):
///   - TrialSummary_batch_{ts}.csv  ← the one the thesis results section reads
///   - Performance_batch_{ts}.csv
///   - GoalDistribution_batch_{ts}.csv
///   - EventLog_batch_{ts}.csv
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    [Header("Batch Configuration")]
    [Tooltip("Max duration per trial (seconds) before a TimeLimit outcome.")]
    [SerializeField] private float experimentDuration = 60f;

    [Tooltip("Number of repeat trials per condition per agent-count. 10 gives usable stddev on most metrics.")]
    [SerializeField] private int runsPerCondition = 10;

    [Tooltip("Base seed. Each trial uses baseSeed + trialIndex where trialIndex is unique across sizes+runs.")]
    [SerializeField] private int baseSeed = 42;

    [Tooltip("Delay between trials for scene cleanup (seconds).")]
    [SerializeField] private float transitionDelay = 2f;

    [Tooltip("Wall-clock seconds between recording start and ForceStimulusOnAllFlocks(). " +
             "Prevents spawn-startup events from leaking into reaction-time measurements " +
             "(see methodology-revisions-2026-04 item 5). Defaults to 5s, matching the " +
             "warmup-trim window used for FPS/CPU steady-state analysis. Reaction-time " +
             "recording is anchored at stimulus fire, so the warmup + reaction-time clock " +
             "are aligned: everything before t=warmup is spawn-startup data, everything " +
             "after is steady-state.")]
    [SerializeField] private float stimulusWarmupSec = 5f;

    [Tooltip("Which conditions to compare.")]
    [SerializeField] private AgentCondition[] conditionsToTest = new[]
    {
        AgentCondition.PureBOIDS,
        AgentCondition.BOIDSWithGOAPLeader,
        AgentCondition.GOAPWithBOIDSMovement
    };

    [Tooltip("Agent counts to sweep. Default [100] reproduces the original single-size batch. " +
             "[50, 100, 200, 400] adds a 4-point scaling curve. " +
             "Total trials = modes × sizes × runs × conditions.")]
    [SerializeField] private int[] agentCountsToTest = new[] { 100 };

    [Tooltip("Benchmark modes to run. Combat (default) = current behaviour, damage on, " +
             "variable duration. Stress = invulnerable player + flocks, fixed duration; " +
             "isolates RQ1 perf measurement from the death-rate confound. Add Stress to " +
             "the array (alongside or instead of Combat) before final batches.")]
    [SerializeField] private BenchmarkMode[] modesToTest = new[] { BenchmarkMode.Combat };

    [Header("Output")]
    [Tooltip("Directory for CSV output. Defaults to ExperimentResults/ in the project root.")]
    [SerializeField] private string outputDirectory = "";

    [Header("Status (read-only)")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private int currentRunIndex = -1;
    [SerializeField] private int currentConditionIndex = -1;
    [SerializeField] private float timeRemaining = 0f;
    [SerializeField] private string lastOutcome = "";

    [Header("Automated Player")]
    [Tooltip("Reference to the AutomatedPlayer on the Player GameObject. Enabled during trials.")]
    [SerializeField] private AutomatedPlayer automatedPlayer;

    [Header("Required Dependencies (auto-found from this GameObject if empty)")]
    [Tooltip("Drag in if not on the same GameObject as ExperimentRunner.")]
    [SerializeField] private ConditionManager conditionManager;
    [SerializeField] private BehavioralMetricsCollector metricsCollector;
    [SerializeField] private PerformanceProfiler performanceProfiler;

    private PlayerHealth playerHealth;

    private void Awake()
    {
        // Fall back to same-GameObject components if Inspector slots are empty.
        if (conditionManager == null) conditionManager = GetComponent<ConditionManager>();
        if (metricsCollector == null) metricsCollector = GetComponent<BehavioralMetricsCollector>();
        if (performanceProfiler == null) performanceProfiler = GetComponent<PerformanceProfiler>();

        // Then try finding them anywhere in the scene as a second fallback.
        if (conditionManager == null) conditionManager = FindFirstObjectByType<ConditionManager>();
        if (metricsCollector == null) metricsCollector = FindFirstObjectByType<BehavioralMetricsCollector>();
        if (performanceProfiler == null) performanceProfiler = FindFirstObjectByType<PerformanceProfiler>();

        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "ExperimentResults"));
        System.IO.Directory.CreateDirectory(outputDirectory);

        if (conditionManager != null && conditionManager.playerTransform != null)
            playerHealth = conditionManager.playerTransform.GetComponent<PlayerHealth>();
    }

    /// <summary>Entry point — kicks off the full batch.</summary>
    public void RunAllExperiments()
    {
        if (isRunning)
        {
            Debug.LogWarning("[ExperimentRunner] Already running!");
            return;
        }
        var missing = new System.Collections.Generic.List<string>();
        if (conditionManager == null) missing.Add(nameof(ConditionManager));
        if (metricsCollector == null) missing.Add(nameof(BehavioralMetricsCollector));
        if (performanceProfiler == null) missing.Add(nameof(PerformanceProfiler));
        if (missing.Count > 0)
        {
            Debug.LogError(
                $"[ExperimentRunner] Missing {string.Join(", ", missing)}. " +
                "Attach the missing component(s) to this GameObject, or drag a reference into the " +
                "Inspector under \"Required Dependencies\" on the ExperimentRunner.");
            return;
        }
        if (runsPerCondition < 1)
        {
            Debug.LogError("[ExperimentRunner] runsPerCondition must be ≥ 1.");
            return;
        }

        StartCoroutine(RunBatchCoroutine());
    }

    private IEnumerator RunBatchCoroutine()
    {
        isRunning = true;
        string batchStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Shared paths — all trials in the batch append to the same 4 files.
        string summaryPath     = System.IO.Path.Combine(outputDirectory, $"TrialSummary_batch_{batchStamp}.csv");
        string performancePath = System.IO.Path.Combine(outputDirectory, $"Performance_batch_{batchStamp}.csv");
        string goalsPath       = System.IO.Path.Combine(outputDirectory, $"GoalDistribution_batch_{batchStamp}.csv");
        string eventsPath      = System.IO.Path.Combine(outputDirectory, $"EventLog_batch_{batchStamp}.csv");

        // Guard against an empty sweep array — fall back to a single run at whatever
        // ConditionManager currently has configured so Play-from-Inspector still works.
        int[] sizes = agentCountsToTest;
        if (sizes == null || sizes.Length == 0) sizes = new[] { conditionManager.AgentCount };

        BenchmarkMode[] modes = modesToTest;
        if (modes == null || modes.Length == 0) modes = new[] { BenchmarkMode.Combat };

        int totalTrials = modes.Length * sizes.Length * runsPerCondition * conditionsToTest.Length;
        Debug.Log($"[ExperimentRunner] Starting batch: {totalTrials} trials " +
                  $"({modes.Length} mode(s) × {sizes.Length} size(s) × {runsPerCondition} runs × {conditionsToTest.Length} conditions), " +
                  $"duration ≤ {experimentDuration}s each. BaseSeed={baseSeed}.");
        // Echo the actual sweep config so a stale Inspector value or wrong field
        // edit is obvious from the first line of console output.
        Debug.Log($"[ExperimentRunner] Sweep: modes=[{string.Join(",", modes)}] " +
                  $"sizes=[{string.Join(",", sizes)}] " +
                  $"conditions=[{string.Join(",", conditionsToTest)}] " +
                  $"runsPerCondition={runsPerCondition}");

        // Track per-(mode, size, condition) aggregates for the final text summary.
        var perGroup = new Dictionary<(BenchmarkMode mode, int size, AgentCondition cond), List<TrialResult>>();

        // Mode-outer, size-middle, run/condition-inner.
        // Rationale: each mode's full data set completes before the next mode begins,
        // so if the batch is interrupted we still have a complete set for the priority
        // mode (combat by default; CJ reorders modesToTest to prioritise stress for
        // RQ1-focused runs). Within a mode, smaller sizes complete first.
        // Seed is stable across modes for the same (size, run) so combat and stress
        // trials with the same seed share initial conditions — they are paired
        // observations rather than independent samples, which strengthens RQ1
        // comparisons against the same starting state.
        for (int modeIdx = 0; modeIdx < modes.Length; modeIdx++)
        {
            var mode = modes[modeIdx];
            Debug.Log($"[ExperimentRunner] === mode {mode} ({modeIdx + 1}/{modes.Length}) ===");

        for (int sizeIdx = 0; sizeIdx < sizes.Length; sizeIdx++)
        {
            int size = sizes[sizeIdx];
            conditionManager.SetAgentCount(size);

            Debug.Log($"[ExperimentRunner] --- size {size} ({sizeIdx + 1}/{sizes.Length}) ---");

            for (int run = 0; run < runsPerCondition; run++)
            {
                currentRunIndex = run;
                // Unique seed per (size, run); identical across modes so a stress trial
                // and combat trial with the same (size, run) share initial conditions.
                int seed = baseSeed + sizeIdx * runsPerCondition + run;

                for (int c = 0; c < conditionsToTest.Length; c++)
                {
                    currentConditionIndex = c;
                    var condition = conditionsToTest[c];

                    Debug.Log($"[ExperimentRunner] === mode={mode} size={size} run={run}/{runsPerCondition - 1} cond={condition} seed={seed} ===");

                    var result = new TrialResult
                    {
                        mode = mode,
                        condition = condition,
                        agentCount = size,
                        runId = run,
                        seed = seed
                    };
                    yield return RunSingleTrial(
                        mode, condition, size, run, seed,
                        summaryPath, performancePath, goalsPath, eventsPath,
                        result);

                    var key = (mode, size, condition);
                    if (!perGroup.ContainsKey(key)) perGroup[key] = new List<TrialResult>();
                    perGroup[key].Add(result);

                    yield return new WaitForSeconds(transitionDelay);
                }
            }
        }
        }

        currentRunIndex = -1;
        currentConditionIndex = -1;
        isRunning = false;
        if (automatedPlayer != null) automatedPlayer.enabled = false;

        LogBatchSummary(perGroup, summaryPath);
    }

    private IEnumerator RunSingleTrial(
        BenchmarkMode mode,
        AgentCondition condition, int configuredAgentCount, int runId, int seed,
        string summaryPath, string performancePath, string goalsPath, string eventsPath,
        TrialResult result)
    {
        // Reset everything from the previous trial.
        conditionManager.ClearAgents();
        conditionManager.SetSeed(seed); // reseeds Random.InitState + serialized field
        conditionManager.CurrentCondition = condition;
        conditionManager.SpawnAgentsForCondition();

        // Re-resolve playerHealth every trial — Awake-time lookup misses the ref
        // when ConditionManager.playerTransform is wired later. Tag lookup is the
        // same pattern the visual tests use.
        if (playerHealth == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) playerHealth = playerGO.GetComponent<PlayerHealth>();
        }

        if (playerHealth != null) playerHealth.ResetHealth();
        if (automatedPlayer != null) automatedPlayer.enabled = true;

        // Apply benchmark mode AFTER spawn (so the SetStressMode FindObjects sweep
        // catches every flock manager just created) and BEFORE recording starts.
        // In stress mode the player and all flocks are now invulnerable, so
        // PlayerDeath / FlockWiped early-exits in the trial loop below cannot fire
        // — stress trials always run to TimeLimit, giving a fixed 60s window of
        // post-warmup data at constant live agent count.
        conditionManager.SetStressMode(mode == BenchmarkMode.Stress);

        // Start recording BEFORE the settle delay so any stimuli that fire during
        // scene settling (FlockManager.SetTarget from aggro triggers, first
        // GoalChange from brains waking up) land inside the recording window.
        // Previously these were dropped because recording started after the delay,
        // breaking the reaction-time metric for most trials.
        metricsCollector.StartRecording(runId, seed, mode);
        performanceProfiler.StartTrial(runId, seed, mode);
        performanceProfiler.ClearMetrics();

        // Belt-and-suspenders: the aggro trigger only fires OnTriggerEnter, which
        // may miss the case where the player is ALREADY inside the trigger at
        // spawn (no collision event). Force the stimulus explicitly so every
        // trial has a deterministic stimulus timestamp for reaction-time math.
        yield return null; // let Start() fire on spawned FlockManagers / GOAPBoidFlockManager

        // Stimulus warmup gate (methodology-revisions-2026-04 item 5).
        // Pre-fix, the stimulus fired ~1 frame after recording start, which let
        // spawn-startup events (initial flock-state transitions, brain wake-up
        // GoalChanges) contaminate the reaction-time metric — visible in the
        // 2026-04-24 batch as bimodal distributions with σ > μ at small N (e.g.
        // PureBOIDS-25 σ=3893ms on μ=2766ms). Delaying the stimulus past the
        // spawn-startup window means the reaction clock starts after the agents
        // have settled, so the metric measures actual response latency rather
        // than spawn-induced noise. The warmup also matches the notebook's
        // FPS/CPU trim window — one defensible warmup constant for the thesis.
        if (stimulusWarmupSec > 0f)
            yield return new WaitForSeconds(stimulusWarmupSec);

        ForceStimulusOnAllFlocks();

        // Now let the scene settle. Events during this window are still recorded.
        yield return new WaitForSeconds(transitionDelay);

        // Trial loop — exits on any termination condition.
        float elapsed = 0f;
        string outcome = "TimeLimit";
        timeRemaining = experimentDuration;

        while (elapsed < experimentDuration)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                outcome = "PlayerDeath";
                break;
            }
            if (conditionManager.AllFlocksDead)
            {
                outcome = "FlockWiped";
                break;
            }

            elapsed += Time.deltaTime;
            timeRemaining = experimentDuration - elapsed;
            yield return null;
        }
        lastOutcome = outcome;

        // Capture metrics BEFORE stopping so snapshot/events include the final frame.
        metricsCollector.StopRecording();

        // Per-trial reaction times (by flockId: 0 = melee, 1 = ranged for GOAPBoid;
        // for PureBOIDS/Leader the flockId is cast from FlockType, so 0/1 too).
        float reactionMelee = metricsCollector.GetReactionTimeMs(0);
        float reactionRanged = metricsCollector.GetReactionTimeMs(1);
        float entropy = metricsCollector.ComputeGoalEntropy();
        var perfAgg = performanceProfiler.ComputeTrialAggregates();

        // Phase D — memory aggregates threaded through so §5.3 (Memory Allocation)
        // has data without a separate collection pass. Combat-effectiveness metrics
        // (item 2) read off the BehavioralMetricsCollector accumulators populated
        // during the trial. DamagePerSecondToPlayer uses `elapsed` (the trial-loop
        // duration, == DurationSec in the CSV) as its denominator so a reader
        // computing TotalDamageToPlayer/DurationSec gets the same value.
        float totalDmg = metricsCollector.TotalDamageToPlayer;
        float dpsToPlayer = elapsed > 0f ? totalDmg / elapsed : 0f;
        metricsCollector.AppendTrialSummaryRow(
            summaryPath, mode.ToString(), condition.ToString(), configuredAgentCount, runId, seed,
            outcome, elapsed,
            reactionMelee, reactionRanged, entropy,
            perfAgg.avgFPS, perfAgg.avgCpuTimeMs, perfAgg.avgCpuTimeMsPerAgent, perfAgg.maxAgentCount,
            perfAgg.avgGcMemoryMB, perfAgg.avgMonoUsedMB, perfAgg.avgTotalAllocatedMB,
            totalDmg, dpsToPlayer,
            metricsCollector.FirstHitMs, metricsCollector.AgentsKilledByPlayer);

        metricsCollector.ExportGoalDistribution(goalsPath);
        metricsCollector.ExportEventLog(eventsPath);
        performanceProfiler.ExportMetrics(performancePath);

        // Snapshot the trial result for the text summary.
        result.outcome = outcome;
        result.duration = elapsed;
        result.reactionMelee = reactionMelee;
        result.reactionRanged = reactionRanged;
        result.entropy = entropy;
        result.avgFPS = perfAgg.avgFPS;
        result.avgCpuMs = perfAgg.avgCpuTimeMs;
        result.avgCpuMsPerAgent = perfAgg.avgCpuTimeMsPerAgent;
        result.maxAgentCount = perfAgg.maxAgentCount;

        if (automatedPlayer != null) automatedPlayer.enabled = false;
        Debug.Log($"[ExperimentRunner] Trial done: {condition} run={runId} outcome={outcome} duration={elapsed:F1}s entropy={entropy:F2}");
    }

    private void LogBatchSummary(
        Dictionary<(BenchmarkMode mode, int size, AgentCondition cond), List<TrialResult>> perGroup,
        string summaryPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ExperimentRunner] Batch complete.");
        sb.AppendLine($"Summary CSV: {summaryPath}");
        sb.AppendLine("");
        sb.AppendLine("Mode   | Size | Condition                  | trials | AvgFPS (μ±σ) | CPU ms/agent (μ±σ) | Entropy (μ±σ) | React melee ms (μ±σ)");

        // Sort by (mode, size, condition) so the table reads top-to-bottom in sweep order.
        var keys = new List<(BenchmarkMode mode, int size, AgentCondition cond)>(perGroup.Keys);
        keys.Sort((a, b) =>
        {
            int byM = a.mode.CompareTo(b.mode);
            if (byM != 0) return byM;
            int byS = a.size.CompareTo(b.size);
            return byS != 0 ? byS : a.cond.CompareTo(b.cond);
        });

        foreach (var key in keys)
        {
            var results = perGroup[key];
            if (results.Count == 0) continue;
            var fps = MeanStd(results, r => r.avgFPS);
            var cpu = MeanStd(results, r => r.avgCpuMsPerAgent);
            var ent = MeanStd(results, r => r.entropy);
            var rxn = MeanStd(results, r => r.reactionMelee < 0 ? 0f : r.reactionMelee);
            sb.AppendLine($"{key.mode,-6} | {key.size,4} | {key.cond,-26} | {results.Count,6} | {fps.mean,6:F1}±{fps.std,5:F1} | {cpu.mean,7:F3}±{cpu.std,5:F3}     | {ent.mean,5:F2}±{ent.std,4:F2} | {rxn.mean,7:F1}±{rxn.std,5:F1}");
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Calls SetTarget / NotifyTargetAcquired on every flock in the scene with the
    /// player's transform. Guarantees a StimulusAcquired event inside the recording
    /// window even when the aggro trigger wouldn't fire (player already inside, or
    /// trigger disabled). Needed so the reaction-time metric gets a deterministic
    /// starting timestamp.
    /// </summary>
    private void ForceStimulusOnAllFlocks()
    {
        Transform player = null;
        if (playerHealth != null) player = playerHealth.transform;
        if (player == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) player = playerGO.transform;
        }
        if (player == null) return;

        foreach (var fm in FindObjectsByType<FlockManager>(FindObjectsSortMode.None))
            fm.SetTarget(player);

        foreach (var gbfm in FindObjectsByType<GOAPBoidFlockManager>(FindObjectsSortMode.None))
            gbfm.NotifyTargetAcquired(player);
    }

    private static (float mean, float std) MeanStd(List<TrialResult> results, System.Func<TrialResult, float> accessor)
    {
        if (results.Count == 0) return (0, 0);
        double sum = 0; foreach (var r in results) sum += accessor(r);
        double mean = sum / results.Count;
        double sqSum = 0; foreach (var r in results) { double d = accessor(r) - mean; sqSum += d * d; }
        double std = results.Count > 1 ? System.Math.Sqrt(sqSum / (results.Count - 1)) : 0;
        return ((float)mean, (float)std);
    }

    private class TrialResult
    {
        public BenchmarkMode mode;
        public AgentCondition condition;
        public int agentCount;
        public int runId;
        public int seed;
        public string outcome = "";
        public float duration;
        public float reactionMelee;
        public float reactionRanged;
        public float entropy;
        public float avgFPS;
        public float avgCpuMs;
        public float avgCpuMsPerAgent;
        public int maxAgentCount;
    }

    #if UNITY_EDITOR
    [ContextMenu("Run All Experiments")]
    private void EditorRunAll()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ExperimentRunner] Must be in Play Mode!");
            return;
        }
        RunAllExperiments();
    }

    [ContextMenu("Stop Experiment")]
    private void EditorStop()
    {
        StopAllCoroutines();
        if (metricsCollector != null) metricsCollector.StopRecording();
        isRunning = false;
        currentRunIndex = -1;
        currentConditionIndex = -1;
        Debug.Log("[ExperimentRunner] Batch stopped.");
    }
    #endif
}
