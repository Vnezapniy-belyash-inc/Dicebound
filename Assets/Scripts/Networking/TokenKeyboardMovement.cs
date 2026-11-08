using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class TokenController
{
    private static TokenController _keyboardToken;
    private Vector3 KeyboardStepPosition => IsPredictingStep ? DisplayPosition : CommittedPosition;

    public static bool ArrowKeysControlToken => _keyboardToken != null
        && _keyboardToken.IsSpawned && !_keyboardToken.IsServer
        && _keyboardToken._netMovementPolicy.Value == (int)PlayerMovementPolicy.Step
        && _keyboardToken.CanStartLocalDrag && GameplayInputGate.AllowsKeyboardHotkeys
        && !SceneEditor.IsEditing && !FogManager.IsManualEditing
        && !(MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive)
        && !(EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive);

    public bool IsKeyboardMovementSelected => _keyboardToken==this && ArrowKeysControlToken;

    private void UpdateKeyboardMovement()
    {
        if (!IsSpawned || IsServer) return;
        // Default to the player's hero; clicking another controllable token selects it.
        if ((_keyboardToken == null || !_keyboardToken.IsSpawned || !_keyboardToken.CanStartLocalDrag)
            && IsHero && CanStartLocalDrag && ControllerClientId == NetworkManager.LocalClientId)
            _keyboardToken = this;
        if (_keyboardToken != this || !ArrowKeysControlToken) return;
        if(GetComponent<TokenKeyboardIndicator>()==null)gameObject.AddComponent<TokenKeyboardIndicator>();
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        Vector2Int direction = keyboard.upArrowKey.wasPressedThisFrame ? Vector2Int.up
            : keyboard.downArrowKey.wasPressedThisFrame ? Vector2Int.down
            : keyboard.leftArrowKey.wasPressedThisFrame ? Vector2Int.left
            : keyboard.rightArrowKey.wasPressedThisFrame ? Vector2Int.right : Vector2Int.zero;
        if (direction != Vector2Int.zero)
        {
            var grid=FindAnyObjectByType<GridManager>();
            if (grid != null)
                RequestKeyboardStep(ScreenDirectionToGridStep(Camera.main, grid.GridRotation, KeyboardStepPosition, direction));
        }
    }

    /// <summary>Choose the adjacent cell which points along the pressed arrow on this player's screen.</summary>
    public static Vector2Int ScreenDirectionToGridStep(Camera camera,Quaternion gridRotation,Vector3 position,Vector2Int screenDirection)
    {
        if(camera==null||screenDirection==Vector2Int.zero)return Vector2Int.zero;
        var origin=camera.WorldToScreenPoint(position);
        if(origin.z<=0)return Vector2Int.zero;
        Vector2Int best=Vector2Int.zero;
        float score=0;
        foreach(var step in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
        {
            var projected=camera.WorldToScreenPoint(position+gridRotation*new Vector3(step.x,0,step.y));
            Vector2 delta=new Vector2(projected.x-origin.x,projected.y-origin.y);
            if(projected.z<=0||delta.sqrMagnitude<.000001f)continue;
            float alignment=Vector2.Dot(delta.normalized,(Vector2)screenDirection);
            if(alignment>score){score=alignment;best=step;}
        }
        return best;
    }

    public bool RequestKeyboardStep(Vector2Int direction)
    {
        if (!ArrowKeysControlToken || _keyboardToken != this || !IsCardinalStep(direction)) return false;
        var grid = FindAnyObjectByType<GridManager>();
        if (grid == null) return false;
        Vector3 position = KeyboardStepPosition;
        Vector2Int from = grid.GetGridPosition(position);
        Vector2Int to = from + direction;
        if (from.x < 0 || !IsStepCellInBounds(grid, to)) return false;
        int gesture = NetworkDraggable.NextGesture();
        if (!PredictStep(gesture, grid.GetCellCenter(to.x, to.y, position.y))) return false;
        KeyboardStepRpc(gesture, from, direction);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void KeyboardStepRpc(int gesture, Vector2Int from, Vector2Int direction, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        var grid = FindAnyObjectByType<GridManager>();
        // If a preceding prediction failed or the master moved the token, discard steps
        // based on the old cell instead of applying them from an unintended position.
        if (SceneFileStore.MovementPolicy != PlayerMovementPolicy.Step || grid == null
            || !IsStepCellInBounds(grid, from) || !IsCardinalStep(direction)
            || grid.GetGridPosition(CommittedPosition) != from)
        {
            RejectFinishRpc(gesture, RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }
        Vector2Int to = from + direction;
        if (!IsStepCellInBounds(grid, to) || !TryBeginServerDrag(sender, gesture))
        {
            RejectFinishRpc(gesture, RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }
        FinishOnServer(sender, gesture, grid.GetCellCenter(to.x, to.y, CommittedPosition.y),
            Vector3.zero, Vector3.zero, false);
    }

    private static bool IsCardinalStep(Vector2Int step) =>
        step == Vector2Int.up || step == Vector2Int.down || step == Vector2Int.left || step == Vector2Int.right;

    private static bool IsStepCellInBounds(GridManager grid, Vector2Int cell) =>
        cell.x >= 0 && cell.y >= 0 && cell.x < grid.Width && cell.y < grid.Height;
}
