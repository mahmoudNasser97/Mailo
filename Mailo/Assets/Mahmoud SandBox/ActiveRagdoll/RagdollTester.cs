using UnityEngine;

/// <summary>
/// Add to the Player GameObject to drive and test the active ragdoll.
/// Controls:
///   WASD        — walk (sets GaitController input)
///   K           — force knockdown (simulates a massive hit)
///   G           — force get-up immediately
///   F2/F3/F4    — set wind strength (0 / medium / gust)
///   F1          — toggle HUD overlay
/// </summary>
public class RagdollTester : MonoBehaviour
{
    ActiveRagdollController _ragdoll;
    BalanceController       _balance;
    GaitController          _gait;

    bool _showHUD = true;

    void Awake()
    {
        _ragdoll = GetComponent<ActiveRagdollController>();
        _balance = GetComponent<BalanceController>();
        _gait    = GetComponent<GaitController>();

        if (_ragdoll == null) Debug.LogError("[RagdollTester] No ActiveRagdollController on Player.");
        if (_balance == null) Debug.LogError("[RagdollTester] No BalanceController on Player.");
        if (_gait    == null) Debug.LogError("[RagdollTester] No GaitController on Player.");
    }

    void Update()
    {
        // ── Walking input (world-space WASD) ──────────────────────────────
        Vector3 dir = new Vector3(
            Input.GetAxisRaw("Horizontal"),
            0f,
            Input.GetAxisRaw("Vertical")
        ).normalized;

        if (_gait != null)
        {
            _gait.DesiredMoveDirection = dir;
            _gait.DesiredMoveSpeed     = dir.sqrMagnitude > 0.01f ? 2.5f : 0f;
        }

        // ── Force knockdown ───────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.K) && _ragdoll != null)
            _ragdoll.OnBoneImpact(9999f);

        // ── Force get-up (skip settle wait) ──────────────────────────────
        if (Input.GetKeyDown(KeyCode.G) && _ragdoll != null)
            _ragdoll.ForceGetUp();

        // ── Wind presets ──────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.F2) && _ragdoll != null)
        {
            _ragdoll.windBaseStrength = 0f;
            _ragdoll.windGustStrength = 0f;
        }
        if (Input.GetKeyDown(KeyCode.F3) && _ragdoll != null)
        {
            _ragdoll.windBaseStrength = 60f;
            _ragdoll.windGustStrength = 120f;
        }
        if (Input.GetKeyDown(KeyCode.F4) && _ragdoll != null)
        {
            _ragdoll.windBaseStrength = 150f;
            _ragdoll.windGustStrength = 300f;
        }

        // ── HUD toggle ────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.F1)) _showHUD = !_showHUD;
    }

    void OnGUI()
    {
        if (!_showHUD || _ragdoll == null) return;

        var style  = new GUIStyle(GUI.skin.box)  { fontSize = 14, alignment = TextAnchor.UpperLeft };
        var label  = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        label.normal.textColor = Color.white;

        GUI.Box(new Rect(8, 8, 320, 240), "", style);

        float y = 16f;
        void Line(string text) { GUI.Label(new Rect(16, y, 300, 22), text, label); y += 22; }

        string stateColor = _ragdoll.CurrentState switch
        {
            RagdollState.Balanced    => "<color=lime>",
            RagdollState.Ragdoll     => "<color=red>",
            RagdollState.GettingUp   => "<color=yellow>",
            _                        => "<color=white>"
        };
        label.richText = true;
        Line($"State:    {stateColor}{_ragdoll.CurrentState}</color>");
        Line($"Strength: {_ragdoll.MuscleStrength:F2}");

        string windLabel = _ragdoll.windBaseStrength <= 0f ? "<color=cyan>OFF</color>"
            : _ragdoll.windBaseStrength <= 60f            ? "<color=yellow>MEDIUM</color>"
                                                          : "<color=red>STRONG</color>";
        Line($"Wind:     {windLabel}  ({_ragdoll.windBaseStrength:F0} N)");

        if (_balance != null)
        {
            Vector3 cp = _balance.CapturePoint();
            Vector3 sc = _balance.SupportCenter();
            Line($"CapturePoint: ({cp.x:F2}, {cp.z:F2})");
            Line($"SupportCentr: ({sc.x:F2}, {sc.z:F2})");
        }

        if (_gait != null)
        {
            Line($"MoveDir:  {_gait.DesiredMoveDirection}");
            Line($"Speed:    {_gait.DesiredMoveSpeed:F1} m/s");
        }

        y += 6;
        label.richText = false;
        label.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        Line("K=knockdown  G=getup  F2/F3/F4=wind  F1=HUD");
    }
}
