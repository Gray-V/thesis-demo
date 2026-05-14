using UnityEditor;
using UnityEngine;

/// <summary>
/// Toggle for the FlockManager.DiagnosticLogging flag — investigates the
/// 20-agent equilibrium floor seen in BOIDSWithGOAPLeader (see
/// wiki/methodology-revisions-2026-04.md item 3a result, point 3).
///
/// When ON, each FlockManager logs:
///   - one [FloorDiag/Spawn] line per flock at Start with originalFlockSize,
///     overrides, EffectiveMaxHP, and computed floor.
///   - [FloorDiag/Cull] each time SyncBoidCountToHealth picks a new target,
///     showing hp%, originalFlockSize, and delta to be culled.
///   - [FloorDiag/Wipe] when KillAllBoids fires, showing boidsAtWipe + hp.
///
/// Persistence: the toggle value is mirrored to EditorPrefs so domain reloads
/// (Play-mode entry with "Reload Domain" enabled, script recompile, editor
/// restart) re-apply the user's choice. Without this, the static field's
/// declared default (false) clobbers the user's toggle on every Play.
/// Remove this menu and the flag once the root cause is pinned down.
/// </summary>
public static class FloorDiagnosticMenu
{
    private const string MenuPath = "Thesis/Toggle Floor Diagnostic";
    private const string EditorPrefsKey = "ThesisBrain.FloorDiagnosticLogging";

    [InitializeOnLoadMethod]
    private static void RestoreFromEditorPrefs()
    {
        FlockManager.DiagnosticLogging = EditorPrefs.GetBool(EditorPrefsKey, false);
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        FlockManager.DiagnosticLogging = !FlockManager.DiagnosticLogging;
        EditorPrefs.SetBool(EditorPrefsKey, FlockManager.DiagnosticLogging);
        Debug.Log($"[FloorDiag] FlockManager.DiagnosticLogging = {FlockManager.DiagnosticLogging}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, FlockManager.DiagnosticLogging);
        return true;
    }
}

/// <summary>
/// Toggle for <see cref="LeaderGoapBrain.DiagnosticLogging"/> — investigates the
/// v1 batch (2026-05-05) finding that BOIDSWithGOAPLeader has a median melee
/// reaction time of 857–2602 ms across N, vs PureBOIDS 2–43 ms and
/// GOAPWithBOIDSMovement 5–59 ms. See wiki/experiments/v1-batch-2026-05-05.md
/// surprises section and wiki/todos.md item #1.
///
/// When ON, the leader emits four event types into the EventLog CSV that
/// reconstruct a four-bucket reaction-time budget per stimulus:
///   - StimulusAcquired (already always emitted by FlockManager.SetTarget)
///   - LeaderPerceives (rising edge of leader's own playerNearby check)
///   - GoalChange (already always emitted by LeaderGoapBrain on goal switch)
///   - LeaderActionStart (emitted by each leader Action's Start() override)
///   - FollowerReact (emitted by FlockManager on the first cohesion redirect
///     after a leader goal change)
///
/// Persistence: the toggle value is mirrored to EditorPrefs and re-applied via
/// <see cref="RestoreFromEditorPrefs"/> after every domain reload. Without
/// this, Unity 6's default Enter Play Mode → Reload Domain re-runs the static
/// field initialiser (resetting DiagnosticLogging to false) before any trial
/// code runs, silently producing diagnostic-event-free EventLog CSVs even
/// though the menu showed the toggle as on. Three batches landed without
/// diagnostic events on 2026-05-05 before this was diagnosed.
/// </summary>
public static class LeaderDiagnosticMenu
{
    private const string MenuPath = "Thesis/Toggle Leader Diagnostic";
    private const string EditorPrefsKey = "ThesisBrain.LeaderDiagnosticLogging";

    [InitializeOnLoadMethod]
    private static void RestoreFromEditorPrefs()
    {
        LeaderGoapBrain.DiagnosticLogging = EditorPrefs.GetBool(EditorPrefsKey, false);
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        LeaderGoapBrain.DiagnosticLogging = !LeaderGoapBrain.DiagnosticLogging;
        EditorPrefs.SetBool(EditorPrefsKey, LeaderGoapBrain.DiagnosticLogging);
        Debug.Log($"[LeaderDiag] LeaderGoapBrain.DiagnosticLogging = {LeaderGoapBrain.DiagnosticLogging}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, LeaderGoapBrain.DiagnosticLogging);
        return true;
    }
}
