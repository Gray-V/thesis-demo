using UnityEngine;
using UnityEngine.Profiling;
using System.Collections.Generic;

/// <summary>
/// Tracks performance metrics for the four experimental agent conditions.
/// Measures CPU time, frame rate, and memory usage.
///
/// Usage: Attach to ConditionManager GameObject. Metrics are automatically collected each frame.
/// Use ExportMetrics() to save data to CSV.
/// </summary>
public class PerformanceProfiler : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Window size (in seconds) for FPS averaging.")]
    [SerializeField] private float fpsAveragingWindow = 10f;

    [Tooltip("Enable detailed CPU profiling (impacts performance).")]
    [SerializeField] private bool enableDetailedProfiling = true;

    [Header("Current Metrics (Read-Only)")]
    [SerializeField] private float currentFPS;
    [SerializeField] private float averageFPS;
    [SerializeField] private int activeAgentCount;
    [SerializeField] private float cpuTimeMs;
    [SerializeField] private float gcMemoryMB;          // managed heap currently in use
    [SerializeField] private float monoUsedMB;          // Unity Mono runtime memory in use
    [SerializeField] private float totalAllocatedMB;    // total Unity (native + managed) allocations

    private Queue<float> fpsHistory = new Queue<float>();
    private float fpsSum;
    private float lastFrameTime;

    // Per-trial run tags (set by ExperimentRunner before the trial starts).
    private int currentRunId = 0;
    private int currentSeed = 0;
    private string currentMode = "Combat";

    // Per-frame metrics for export
    private List<FrameMetrics> frameMetricsLog = new List<FrameMetrics>();

    private ConditionManager conditionManager;

    /// <summary>
    /// Tags subsequent frames with the given run/seed so batch-exported CSVs
    /// are groupable by trial. Call before starting a trial. Backwards-compatible
    /// overload defaults mode to Combat for callers that don't yet thread it through.
    /// </summary>
    public void StartTrial(int runId, int seed) => StartTrial(runId, seed, BenchmarkMode.Combat);

    public void StartTrial(int runId, int seed, BenchmarkMode mode)
    {
        currentRunId = runId;
        currentSeed = seed;
        currentMode = mode.ToString();
    }

    private void Start()
    {
        lastFrameTime = Time.realtimeSinceStartup;
        conditionManager = GetComponent<ConditionManager>();
    }

    private void Update()
    {
        RecordFrameMetrics();
    }

    /// <summary>
    /// Records metrics for the current frame.
    /// </summary>
    private void RecordFrameMetrics()
    {
        if (enableDetailedProfiling)
            Profiler.BeginSample("PerformanceProfiler.RecordFrameMetrics");

        // Calculate current FPS
        float currentTime = Time.realtimeSinceStartup;
        float deltaTime = currentTime - lastFrameTime;
        lastFrameTime = currentTime;

        if (deltaTime > 0f)
        {
            currentFPS = 1f / deltaTime;
        }

        // Update FPS history for averaging
        fpsHistory.Enqueue(currentFPS);
        fpsSum += currentFPS;

        while (fpsHistory.Count > 0 && (fpsHistory.Count * deltaTime) > fpsAveragingWindow)
        {
            fpsSum -= fpsHistory.Dequeue();
        }

        averageFPS = fpsHistory.Count > 0 ? fpsSum / fpsHistory.Count : currentFPS;

        // Track active agent count
        activeAgentCount = CountActiveAgents();

        // CPU time (approximation via Time.deltaTime)
        cpuTimeMs = Time.deltaTime * 1000f;

        // Memory snapshots (Phase D — for §5.3 Memory Allocation in the thesis).
        // GetTotalMemory(false) reports the managed heap without forcing a GC, so it
        // reflects steady-state managed-side pressure rather than post-collection low.
        // Profiler.GetMonoUsedSizeLong / GetTotalAllocatedMemoryLong report Unity's
        // own memory accounting (Mono managed + total native+managed). All three are
        // O(1) counter reads — safe to call every frame.
        long gcBytes    = System.GC.GetTotalMemory(forceFullCollection: false);
        long monoBytes  = Profiler.GetMonoUsedSizeLong();
        long totalBytes = Profiler.GetTotalAllocatedMemoryLong();
        const float BYTES_PER_MB = 1024f * 1024f;
        gcMemoryMB       = gcBytes    / BYTES_PER_MB;
        monoUsedMB       = monoBytes  / BYTES_PER_MB;
        totalAllocatedMB = totalBytes / BYTES_PER_MB;

        // Log frame data
        float cpuMsPerAgent = activeAgentCount > 0 ? cpuTimeMs / activeAgentCount : 0f;
        frameMetricsLog.Add(new FrameMetrics
        {
            frameNumber = Time.frameCount,
            time = Time.time,
            mode = currentMode,
            condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown",
            runId = currentRunId,
            seed = currentSeed,
            fps = currentFPS,
            averageFPS = averageFPS,
            agentCount = activeAgentCount,
            cpuTimeMs = cpuTimeMs,
            cpuTimeMsPerAgent = cpuMsPerAgent,
            gcMemoryMB       = gcMemoryMB,
            monoUsedMB       = monoUsedMB,
            totalAllocatedMB = totalAllocatedMB,
        });

        if (enableDetailedProfiling)
            Profiler.EndSample();
    }

    /// <summary>
    /// Counts the number of currently active agents in the scene.
    /// </summary>
    private int CountActiveAgents()
    {
        if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.GOAPWithBOIDSMovement)
            return GOAPBoidAgent.AllAgents.Count;

        // BoidAgent-based conditions (PureBOIDS, BOIDSWithGOAPLeader)
        var boids = FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
        if (boids.Length > 0)
            return boids.Length;

        // Fallback for PureGOAP or unknown conditions
        return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Length;
    }

    /// <summary>
    /// Exports all collected metrics to a CSV file.
    /// </summary>
    public void ExportMetrics(string filepath)
    {
        if (frameMetricsLog.Count == 0)
        {
            Debug.LogWarning("[PerformanceProfiler] No metrics to export.");
            return;
        }

        Profiler.BeginSample("PerformanceProfiler.ExportMetrics");

        bool fileExists = System.IO.File.Exists(filepath);
        System.Text.StringBuilder csv = new System.Text.StringBuilder();
        if (!fileExists)
            csv.AppendLine("Frame,Time,BenchmarkMode,Condition,RunId,Seed,FPS,AvgFPS,AgentCount,CPUTimeMs,CPUTimeMsPerAgent,GcMemoryMB,MonoUsedMB,TotalAllocatedMB");

        foreach (var frame in frameMetricsLog)
        {
            csv.AppendLine(
                $"{frame.frameNumber},{frame.time:F3},{frame.mode},{frame.condition},{frame.runId},{frame.seed}," +
                $"{frame.fps:F2},{frame.averageFPS:F2},{frame.agentCount},{frame.cpuTimeMs:F3},{frame.cpuTimeMsPerAgent:F4}," +
                $"{frame.gcMemoryMB:F2},{frame.monoUsedMB:F2},{frame.totalAllocatedMB:F2}");
        }

        try
        {
            if (fileExists)
                System.IO.File.AppendAllText(filepath, csv.ToString());
            else
                System.IO.File.WriteAllText(filepath, csv.ToString());
            Debug.Log($"[PerformanceProfiler] Wrote {frameMetricsLog.Count} frames to: {filepath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PerformanceProfiler] Failed to export metrics: {e.Message}");
        }

        Profiler.EndSample();
    }

    /// <summary>
    /// Clears all collected metrics.
    /// </summary>
    public void ClearMetrics()
    {
        frameMetricsLog.Clear();
        fpsHistory.Clear();
        fpsSum = 0f;
        Debug.Log("[PerformanceProfiler] Metrics cleared.");
    }

    /// <summary>
    /// Returns the current average FPS.
    /// </summary>
    public float GetAverageFPS()
    {
        return averageFPS;
    }

    /// <summary>
    /// Returns the current instantaneous FPS.
    /// </summary>
    public float GetCurrentFPS()
    {
        return currentFPS;
    }

    [System.Serializable]
    private struct FrameMetrics
    {
        public int frameNumber;
        public float time;
        public string mode;
        public string condition;
        public int runId;
        public int seed;
        public float fps;
        public float averageFPS;
        public int agentCount;
        public float cpuTimeMs;
        public float cpuTimeMsPerAgent;
        public float gcMemoryMB;
        public float monoUsedMB;
        public float totalAllocatedMB;
    }

    public struct TrialAggregates
    {
        public float avgFPS;
        public float avgCpuTimeMs;
        public float avgCpuTimeMsPerAgent;
        public int maxAgentCount;
        public float avgGcMemoryMB;
        public float avgMonoUsedMB;
        public float avgTotalAllocatedMB;
    }

    /// <summary>
    /// Aggregates the current buffer of frame metrics into one summary row.
    /// Called by ExperimentRunner at the end of each trial before ClearMetrics.
    /// </summary>
    public TrialAggregates ComputeTrialAggregates()
    {
        var agg = new TrialAggregates();
        if (frameMetricsLog.Count == 0) return agg;

        double sumFps = 0, sumCpu = 0, sumCpuPerAgent = 0;
        double sumGc = 0, sumMono = 0, sumTotal = 0;
        int maxAgents = 0;
        foreach (var f in frameMetricsLog)
        {
            sumFps += f.fps;
            sumCpu += f.cpuTimeMs;
            sumCpuPerAgent += f.cpuTimeMsPerAgent;
            sumGc += f.gcMemoryMB;
            sumMono += f.monoUsedMB;
            sumTotal += f.totalAllocatedMB;
            if (f.agentCount > maxAgents) maxAgents = f.agentCount;
        }
        int n = frameMetricsLog.Count;
        agg.avgFPS = (float)(sumFps / n);
        agg.avgCpuTimeMs = (float)(sumCpu / n);
        agg.avgCpuTimeMsPerAgent = (float)(sumCpuPerAgent / n);
        agg.maxAgentCount = maxAgents;
        agg.avgGcMemoryMB = (float)(sumGc / n);
        agg.avgMonoUsedMB = (float)(sumMono / n);
        agg.avgTotalAllocatedMB = (float)(sumTotal / n);
        return agg;
    }

    #if UNITY_EDITOR
    [ContextMenu("Export Metrics")]
    private void EditorExportMetrics()
    {
        string desktop = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "ExperimentResults"));
        System.IO.Directory.CreateDirectory(desktop);
        string filename = $"PerformanceMetrics_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        string filepath = System.IO.Path.Combine(desktop, filename);
        ExportMetrics(filepath);
    }

    [ContextMenu("Clear Metrics")]
    private void EditorClearMetrics()
    {
        ClearMetrics();
    }
    #endif
}
