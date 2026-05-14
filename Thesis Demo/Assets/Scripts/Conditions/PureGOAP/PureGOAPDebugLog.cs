using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes PureGOAP agent debug info to a log file each frame.
/// Attach to any GameObject in the scene. Logs to ExperimentResults/goap_debug.log.
/// Only logs while agents exist. Samples every 0.5s to keep file manageable.
/// </summary>
public class PureGOAPDebugLog : MonoBehaviour
{
    private string logPath;
    private float nextLogTime;
    private const float LogInterval = 0.5f;
    private StreamWriter writer;
    private int frameCount;

    private void Awake()
    {
        string logDir = System.IO.Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentResults"));
        Directory.CreateDirectory(logDir);
        logPath = Path.Combine(logDir, "goap_debug.log");
        writer = new StreamWriter(logPath, false);
        writer.WriteLine("=== PureGOAP Debug Log ===");
        writer.WriteLine($"Started: {System.DateTime.Now}");
        writer.WriteLine();
        writer.Flush();
    }

    private void Update()
    {
        if (Time.time < nextLogTime) return;
        nextLogTime = Time.time + LogInterval;
        frameCount++;

        var agents = PureGOAPAgent.AllAgents;
        if (agents.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"--- Sample {frameCount} | Time: {Time.time:F2} | Agents: {agents.Count} ---");

        // Shared state
        sb.AppendLine($"  WanderTarget (shared): {WanderTargetSensor.SharedTargetDebug}");

        // Group centroid
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < agents.Count; i++)
            centroid += agents[i].Position;
        if (agents.Count > 0)
            centroid /= agents.Count;
        sb.AppendLine($"  GroupCentroid: {centroid:F1} | Dist(centroid→target): {Vector3.Distance(centroid, WanderTargetSensor.SharedTargetDebug):F1}");

        // Per-agent state
        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            float distToCentroid = Vector3.Distance(a.Position, centroid);
            sb.AppendLine($"  Agent[{i}] pos:{a.Position:F1} vel:{a.velocity:F1} speed:{a.velocity.magnitude:F1} distToCentroid:{distToCentroid:F1}");
        }

        sb.AppendLine();

        writer.Write(sb.ToString());
        writer.Flush();
    }

    private void OnDestroy()
    {
        if (writer != null)
        {
            writer.WriteLine($"=== Ended: {System.DateTime.Now} ===");
            writer.Close();
        }
    }
}
