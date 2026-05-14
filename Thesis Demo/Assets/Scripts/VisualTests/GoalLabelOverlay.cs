using UnityEngine;

/// <summary>
/// On-screen overlay showing each GOAPBoidAgent's current goal above its head.
/// Attach to any scene GameObject (ConditionManager scene works) and press Play.
/// Independent of GOAPVisualTest — useful for debugging normal play-mode runs.
/// </summary>
public class GoalLabelOverlay : MonoBehaviour
{
    [SerializeField] private bool showLabels = true;
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color labelColor = Color.white;
    [SerializeField] private float verticalOffset = 2f;

    private GUIStyle style;

    private void OnGUI()
    {
        if (!showLabels || Camera.main == null) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = labelColor }
            };
        }

        var agents = GOAPBoidAgent.AllAgents;
        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null) continue;
            var brain = a.GetComponent<GOAPBoidBrain>();
            if (brain == null) continue;

            Vector3 world = a.transform.position + Vector3.up * verticalOffset;
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            if (screen.z <= 0f) continue;

            float x = screen.x - 60f;
            float y = Screen.height - screen.y - 10f;
            GUI.Label(new Rect(x, y, 120f, 20f), brain.currentGoalType.ToString(), style);
        }
    }
}
