using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Single source of truth for when gameplay input should be suppressed because the user
/// is editing UI text. All keyboard hotkeys and world-space pointer handlers must consult
/// this gate before acting.
/// </summary>
public static class GameplayInputGate
{
    public static int LastToolExitFrame { get; private set; } = -1;
    public static void MarkToolExit() => LastToolExitFrame = Time.frameCount;
    /// <summary>True while a legacy or TMP text/numeric field has keyboard focus.</summary>
    public static bool IsTextInputFocused
    {
        get
        {
            if (FunEffects.IsStampInputOpen) return true;
            if (TokenController.IsMenuTextFocused) return true;
            var es = EventSystem.current;
            if (es == null) return false;

            var selected = es.currentSelectedGameObject;
            if (selected == null) return false;

            var legacy = selected.GetComponentInParent<InputField>();
            if (legacy != null && legacy.isFocused)
                return true;

            var tmp = selected.GetComponentInParent<TMP_InputField>();
            return tmp != null && tmp.isFocused;
        }
    }

    /// <summary>True when the pointer is over any UI element (EventSystem raycast).</summary>
    public static bool IsPointerOverUI =>
        ActiveToolUI.IsPointerOverPanel || MovementReviewUI.IsOpen || MovementReviewUI.IsPointerOverButton || FunEffects.IsInputOpen ||
        HostSceneCurtain.IsPointerOverSyncPanel ||
        PublicReferenceLibrary.IsOpen || PublicReferenceLibrary.IsPointerOverButton || SceneArrivalPicker.IsOpen || DisplaySettingsUI.IsOpen || DisplaySettingsUI.IsPointerOverButton ||
        TokenController.IsPointerOverMenu ||
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>True while the host has the client curtain down (clients only).</summary>
    public static bool IsHostCurtainBlocking =>
        HostSceneCurtain.IsBlockingLocalPlayer;

    /// <summary>IMGUI overlays (measurement labels, token menus) — blocked for blinded clients.</summary>
    public static bool AllowsImGuiOverlays => !MovementReviewUI.IsOpen && !FunEffects.IsStampInputOpen && !PublicReferenceLibrary.IsOpen && !SceneArrivalPicker.IsOpen && !DisplaySettingsUI.IsOpen && !IsHostCurtainBlocking &&
        !(DiceUI.Instance != null && DiceUI.Instance.IsConfirmationOpen);

    /// <summary>
    /// Keyboard shortcuts: panel toggles, map rotate, camera movement keys, debug keys, etc.
    /// </summary>
    public static bool AllowsKeyboardHotkeys =>
        !MovementReviewUI.IsOpen && !FunEffects.IsInputOpen &&
        !PublicReferenceLibrary.IsOpen && !SceneArrivalPicker.IsOpen && !DisplaySettingsUI.IsOpen && !IsTextInputFocused && !IsHostCurtainBlocking && !HostSceneCurtain.IsLocalSynchronizing &&
        !(DiceUI.Instance != null && DiceUI.Instance.IsConfirmationOpen);

    /// <summary>
    /// Mouse actions on the game world: dice drag, map pan, measurement, pings, token menus.
    /// Blocked while typing or when the cursor is over UI.
    /// </summary>
    public static bool AllowsWorldPointerInput =>
        LastToolExitFrame != Time.frameCount && !DisplaySettingsUI.IsOpen && !IsTextInputFocused && !IsPointerOverUI && !IsHostCurtainBlocking && !HostSceneCurtain.IsLocalSynchronizing;
}
