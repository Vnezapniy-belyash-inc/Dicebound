using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Always-visible tool state, bounded by the viewport and independent of the DM panel.</summary>
public sealed class ActiveToolUI : MonoBehaviour
{
    private static float Left => (DiceUI.Instance?.ToolsPanelPixelWidth ?? 0) + 24;
    private static float Available => Screen.width - Left - 24 - Mathf.Max(DmPanelUI.Instance?.PanelPixelWidth ?? 0, CharacterSheetUI.Instance?.PanelPixelWidth ?? 0);
    private static bool Visible => Available >= 330 && NetworkManager.Singleton?.IsHost == true &&
        (SceneEditor.IsEditing || FogManager.IsManualEditing || FogManager.Instance?.Preview == true);
    private static float Scale => Mathf.Min(LocalUiScale.Value, Available / 620f);
    private static float Top => Mathf.Max(54, (DiceUI.Instance?.SessionHeaderPixelHeight ?? 0) + 6);
    public static bool IsPointerOverPanel => Visible && Mouse.current != null
        && new Rect(Left, Screen.height - Top - 88 * Scale, 620 * Scale, 88 * Scale).Contains(Mouse.current.position.ReadValue());
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => new GameObject("ActiveToolUI").AddComponent<ActiveToolUI>();
    private void Awake() => DontDestroyOnLoad(gameObject);
    private void Update()
    {
        if (NetworkManager.Singleton?.IsHost != true || !GameplayInputGate.AllowsKeyboardHotkeys) return;
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.ctrlKey.isPressed) return;
        if (keyboard.zKey.wasPressedThisFrame) { SceneEditor.Instance?.Deactivate(); FogManager.Instance?.StopManual(); GameMasterUndo.Undo(); }
        if (keyboard.yKey.wasPressedThisFrame) { SceneEditor.Instance?.Deactivate(); FogManager.Instance?.StopManual(); GameMasterUndo.Redo(); }
    }
    private void OnGUI()
    {
        if (!Visible || !GameplayInputGate.AllowsImGuiOverlays) return;
        var matrix = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
        float x = Left / Scale, y = Top / Scale;
        GUI.Box(new Rect(x, y, 620, 88), "", VttUiSkin.ImGuiPanel);
        string state = SceneEditor.IsEditing ? SceneEditor.Instance.Notice : FogManager.IsManualEditing
            ? (FogManager.Instance.ManualMode == 1 ? "Кисть: раскрыть туман" : "Кисть: закрыть туман") + ". ЛКМ — рисовать; ПКМ — закончить; Esc — отменить штрих."
            : "Просмотр глазами игрока: " + FogManager.Instance.PreviewSourceName;
        GUI.Label(new Rect(x + 10, y + 5, 600, 42), state, new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 14 });
        if (GUI.Button(new Rect(x + 10, y + 51, 190, 28), "Отменить · Ctrl+Z", VttUiSkin.ImGuiButton)) { SceneEditor.Instance?.Deactivate(); FogManager.Instance?.StopManual(); GameMasterUndo.Undo(); }
        bool enabled = GUI.enabled; GUI.enabled = GameMasterUndo.NextRedoLabel != "Нет действий";
        if (GUI.Button(new Rect(x + 210, y + 51, 190, 28), "Повторить · Ctrl+Y", VttUiSkin.ImGuiButton)) GameMasterUndo.Redo();
        GUI.enabled = enabled;
        bool editing = SceneEditor.IsEditing || FogManager.IsManualEditing;
        if (GUI.Button(new Rect(x + 410, y + 51, 200, 28), editing ? "Завершить инструмент" : "Вернуться к мастеру", VttUiSkin.ImGuiButton))
        {
            if (editing) { SceneEditor.Instance?.Deactivate(); FogManager.Instance?.StopManual(); }
            else FogManager.Instance?.TogglePreview();
            GameplayInputGate.MarkToolExit();
        }
        GUI.matrix = matrix;
    }
}
