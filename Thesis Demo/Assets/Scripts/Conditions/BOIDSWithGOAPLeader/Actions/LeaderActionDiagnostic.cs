/// <summary>
/// Shared one-line hook each leader Action calls from its <c>Start()</c> override
/// so the four-bucket reaction budget can be reconstructed from the EventLog CSV.
/// Gated on <see cref="LeaderGoapBrain.DiagnosticLogging"/> — zero cost when off.
/// Centralised here (rather than inlined in every action) so the event format
/// stays identical across all eight leader actions, and so the cost of the
/// gate-check stays in one place.
/// </summary>
internal static class LeaderActionDiagnostic
{
    public static void LogStart(string actionName, BoidAgent boid)
    {
        if (!LeaderGoapBrain.DiagnosticLogging) return;
        BehavioralMetricsCollector.Instance?.LogEvent(
            "LeaderActionStart",
            boid != null ? boid.gameObject.name : "?",
            LeaderGoapBrain.ResolveFlockId(boid),
            actionName);
    }
}
