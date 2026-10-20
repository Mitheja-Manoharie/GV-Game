using UnityEngine;

namespace LabLockdown.AI.DroneStalker.Runtime
{
    /// <summary>
    /// Shows what the Drone Stalker is thinking, for testing and for the viva.
    ///   Scene view (gizmos): the route coloured by action, the target tile, the vision cone, where the player was
    ///   last seen, the light map (dark = blue, lit = yellow) and the tiles the player can see (red).
    ///   Play mode (on-screen panel): the action and its reason, awareness, light here, and the five utility scores.
    /// Display only: it reads the agent and changes nothing.
    /// </summary>
    [RequireComponent(typeof(StalkerAgent))]
    public sealed class StalkerDebugView : MonoBehaviour
    {
        [Header("Scene view")]
        [SerializeField] private bool drawRoute = true;
        [SerializeField] private bool drawVisionCone = true;
        [SerializeField] private bool drawLightMap = false;
        [SerializeField] private bool drawPlayerVisibleTiles = true;
        [SerializeField] private float tileGizmoSize = 0.6f;

        [Header("Play mode panel")]
        [SerializeField] private bool showPanel = true;
        [SerializeField] private Vector2 panelPosition = new Vector2(10f, 10f);

        StalkerAgent agent;
        GUIStyle label;

        void Awake() => agent = GetComponent<StalkerAgent>();

        static Color ColourFor(StalkerAction action)
        {
            switch (action)
            {
                case StalkerAction.Patrol: return new Color(0.6f, 0.6f, 0.6f);
                case StalkerAction.Stalk: return new Color(0.3f, 0.7f, 1f);
                case StalkerAction.Hide: return new Color(0.4f, 1f, 0.4f);
                case StalkerAction.Ambush: return new Color(0.8f, 0.4f, 1f);
                case StalkerAction.Strike: return new Color(1f, 0.2f, 0.2f);
                case StalkerAction.Retreat: return new Color(1f, 0.7f, 0.2f);
                default: return Color.white;
            }
        }

        void OnDrawGizmos()
        {
            if (agent == null) agent = GetComponent<StalkerAgent>();
            if (agent == null) return;
            StalkerSensor sensor = agent.Sensor != null ? agent.Sensor : GetComponent<StalkerSensor>();
            if (sensor == null) return;

            if (drawVisionCone) DrawVisionCone(sensor.Eye, agent.Settings);
            if (sensor.GraphProvider == null || sensor.GraphProvider.Graph == null) return;

            if (drawLightMap && sensor.LightMap != null)
            {
                float[] levels = sensor.LightMap.Levels;
                for (int i = 0; i < levels.Length; i++)
                {
                    Gizmos.color = Color.Lerp(new Color(0.1f, 0.2f, 0.9f, 0.6f), new Color(1f, 0.9f, 0.2f, 0.6f), levels[i]);
                    Gizmos.DrawCube(sensor.GraphProvider.NodePosition(i) + Vector3.up * 0.05f, new Vector3(tileGizmoSize, 0.05f, tileGizmoSize));
                }
            }

            StalkerPercepts p = agent.Percepts;
            if (drawPlayerVisibleTiles && p != null && p.PlayerVisibleNodes != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                foreach (int i in p.PlayerVisibleNodes)
                    Gizmos.DrawWireCube(sensor.GraphProvider.NodePosition(i) + Vector3.up * 0.1f, new Vector3(tileGizmoSize, 0.1f, tileGizmoSize));
            }

            StalkerBrain brain = agent.Brain;
            if (brain == null) return;

            if (brain.LastSeenPlayerNode >= 0)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(sensor.GraphProvider.NodePosition(brain.LastSeenPlayerNode) + Vector3.up * 1f, 0.4f);
            }

            if (drawRoute && agent.Decision != null)
            {
                Gizmos.color = ColourFor(agent.CurrentAction);
                Vector3 from = transform.position;
                foreach (Vector3 point in agent.CurrentPath)
                {
                    Vector3 to = point + Vector3.up * 0.5f;
                    Gizmos.DrawLine(from, to);
                    Gizmos.DrawSphere(to, 0.12f);
                    from = to;
                }
                int target = agent.Decision.TargetNode;
                if (target >= 0) Gizmos.DrawWireSphere(sensor.GraphProvider.NodePosition(target) + Vector3.up * 0.5f, 0.5f);
            }
        }

        void DrawVisionCone(Transform eye, StalkerSettings s)
        {
            if (eye == null || s == null) return;
            Gizmos.color = new Color(0.3f, 1f, 1f, 0.5f);
            Vector3 origin = eye.position;
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            float half = s.FieldOfViewDegrees * 0.5f;
            const int segments = 16;
            Vector3 previous = origin + Quaternion.AngleAxis(-half, Vector3.up) * forward * s.SightRangeMetres;
            Gizmos.DrawLine(origin, previous);
            for (int k = 1; k <= segments; k++)
            {
                float angle = -half + s.FieldOfViewDegrees * k / segments;
                Vector3 next = origin + Quaternion.AngleAxis(angle, Vector3.up) * forward * s.SightRangeMetres;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
            Gizmos.DrawLine(origin, previous);
        }

        void OnGUI()
        {
            if (!showPanel || agent == null || agent.Brain == null) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

            StalkerDecision d = agent.Decision;
            StalkerPercepts p = agent.Percepts;
            const float width = 300f, row = 18f;
            float height = row * 13f + 12f;
            GUI.Box(new Rect(panelPosition.x, panelPosition.y, width, height), GUIContent.none);

            float y = panelPosition.y + 6f, x = panelPosition.x + 8f;
            string hex = ColorUtility.ToHtmlStringRGB(ColourFor(agent.CurrentAction));
            GUI.Label(new Rect(x, y, width, row), $"<b>{name}</b>: <color=#{hex}><b>{agent.CurrentAction}</b></color>", label); y += row;
            GUI.Label(new Rect(x, y, width - 16f, row), d != null ? d.Note : "", label); y += row;
            GUI.Label(new Rect(x, y, width, row), $"Aware of player: {agent.Brain.IsAwareOfPlayer}   In sight: {p.PlayerInSight}", label); y += row;
            GUI.Label(new Rect(x, y, width, row), $"Seen by player: {p.PlayerSeesMe}   Light here: {p.LightAt(p.SelfNode):0.00}", label); y += row;
            GUI.Label(new Rect(x, y, width, row), $"Speed: {agent.CurrentSpeed}   Strike: {(d != null && d.StrikeNow ? "NOW" : "-")}", label); y += row + 4f;

            if (d?.Scores == null)
            {
                GUI.Label(new Rect(x, y, width, row), "(no scores while patrolling)", label);
                return;
            }
            foreach (StalkerAction a in ActionScores.All)
            {
                float score = d.Scores[a];
                GUI.Label(new Rect(x, y, 70f, row), a.ToString(), label);
                Color old = GUI.color;
                GUI.color = ColourFor(a);
                GUI.Box(new Rect(x + 72f, y + 3f, Mathf.Clamp(score, 0f, 1.5f) * 120f, row - 6f), GUIContent.none);
                GUI.color = old;
                GUI.Label(new Rect(x + 200f, y, 60f, row), score.ToString("0.00"), label);
                y += row;
            }
        }
    }
}
