using UnityEngine;
using UnityEngine.InputSystem;

public partial class TokenController
{
    private static TokenController _keyboardToken;

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
            if(grid!=null)RequestKeyboardStep(ScreenDirectionToGridStep(Camera.main,grid.GridRotation,CommittedPosition,direction));
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
        if (!ArrowKeysControlToken || _keyboardToken != this || IsLocalDragActiveAny()
            || Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1) return false;
        var grid = FindAnyObjectByType<GridManager>();
        if (grid == null) return false;
        var cell = grid.GetGridPosition(CommittedPosition) + direction;
        if (cell.x < 0 || cell.y < 0 || cell.x >= grid.Width || cell.y >= grid.Height) return false;
        int gesture = NetworkDraggable.NextGesture();
        if (!BeginDrag(gesture)) return false;
        // Reuse the reliable drag lease and completion: the server checks policy, ownership,
        // walls, doors, occupancy and curtain state using the actual RPC sender.
        EndDrag(gesture, grid.GetCellCenter(cell.x, cell.y, CommittedPosition.y));
        return true;
    }
}
