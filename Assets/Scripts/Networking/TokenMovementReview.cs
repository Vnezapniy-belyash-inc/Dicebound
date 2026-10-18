using Unity.Netcode;
using UnityEngine;

public partial class TokenController
{
    private readonly NetworkVariable<int> _netMovementPolicy = new((int)PlayerMovementPolicy.Step);
    private bool _movementPending;
    private int _movementRequestId;
    private ulong _movementClient;
    private Vector3 _movementFrom, _movementTo;
    private double _movementDeadline;
    private PlayerMovementPolicy _movementPolicy;
    public bool HasMovementRequest => IsServer && _movementPending && _movementPolicy == PlayerMovementPolicy.Approval;
    public Vector3 RequestedMovementPosition => _movementTo;
    public float MovementSecondsLeft => Mathf.Max(0, (float)(_movementDeadline - Time.unscaledTimeAsDouble));

    protected override Vector3 ConstrainDragPreview(Vector3 position)
    {
        if (IsServer || _netMovementPolicy.Value != (int)PlayerMovementPolicy.Step) return position;
        var grid = FindAnyObjectByType<GridManager>();
        if (grid == null) return CommittedPosition;
        var from = grid.GetGridPosition(CommittedPosition);
        var point = grid.WorldToGridCoordinates(position);
        var step = MovementRisk.SingleStep(from, new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z)));
        if (step.x < 0 || step.y < 0 || step.x >= grid.Width || step.y >= grid.Height) return CommittedPosition;
        // Local intention uses grid coordinates only, never hidden wall geometry or occupancy.
        return grid.GetCellCenter(step.x, step.y, CommittedPosition.y);
    }

    private bool ReviewPlayerMovement(ulong client, Vector3 destination)
    {
        if (NetworkPermissions.IsHostClient(client)) return false;
        // FinishOnServer temporarily places the object at the requested destination.
        // Evaluate visibility/rights at its committed source, never at an unapproved intention.
        transform.position = _dragStartCommitted;
        // Rights may change during the gesture. A stale lease does not confer permission.
        if (!CanClientStartDrag(client)) { RestorePosition(_dragStartCommitted); return true; }
        var grid = FindAnyObjectByType<GridManager>();
        if (grid == null) { RestorePosition(_dragStartCommitted); return true; }
        if (SceneFileStore.MovementPolicy == PlayerMovementPolicy.Step)
        {
            var from = grid.GetGridPosition(_dragStartCommitted);
            var point = grid.WorldToGridCoordinates(destination);
            var intended = new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z));
            var step = MovementRisk.SingleStep(from, intended);
            var target = grid.GetCellCenter(step.x, step.y, _dragStartCommitted.y);
            var a = grid.WorldToGridCoordinates(_dragStartCommitted);
            var b = grid.WorldToGridCoordinates(target);
            bool allowed = step.x >= 0 && step.y >= 0 && step.x < grid.Width && step.y < grid.Height
                && (step == from || !grid.IsCellOccupied(step))
                && new FogVisibility(SceneEditor.Instance?.Model.Snapshot() ?? new SceneGeometry())
                    .HasLineOfSight(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            if (!allowed)
            {
                RestorePosition(_dragStartCommitted);
                MovementNoticeRpc("Шаг недоступен. Токен остался на месте; мастер может перенести его вручную.", RpcTarget.Single(client, RpcTargetUse.Temp));
                return true;
            }
            transform.position = target;
            return false;
        }
        var cell = grid.GetGridPosition(destination);
        if (cell.x < 0) { RestorePosition(_dragStartCommitted); return true; }
        if (cell != _currentCell) cell = grid.FindNearestFreeCell(cell, 3);
        if (cell.x < 0) { RestorePosition(_dragStartCommitted); return true; }
        destination = grid.GetCellCenter(cell.x, cell.y, _dragStartCommitted.y);
        transform.position = destination;
        var policy = SceneFileStore.MovementPolicy;
        if (policy == PlayerMovementPolicy.Free || !MovementNeedsReview(grid, _dragStartCommitted, destination)) return false;
        _movementFrom = _dragStartCommitted;
        _movementTo = destination;
        _movementClient = client;
        _movementPolicy = policy;
        _movementDeadline = Time.unscaledTimeAsDouble + 30;
        _movementRequestId++;
        _movementPending = true;
        RestorePosition(_movementFrom);
        MovementPromptRpc(_movementRequestId, policy == PlayerMovementPolicy.Approval,
            RpcTarget.Single(client, RpcTargetUse.Temp));
        return true;
    }

    private static bool MovementNeedsReview(GridManager grid, Vector3 from, Vector3 to)
    {
        var a = grid.WorldToGridCoordinates(from); var b = grid.WorldToGridCoordinates(to);
        var start = new Vector2(a.x, a.z); var end = new Vector2(b.x, b.z);
        bool clear = new FogVisibility(SceneEditor.Instance?.Model.Snapshot() ?? new SceneGeometry()).HasLineOfSight(start, end);
        // Sample the whole segment: a known endpoint must not allow jumping over unknown space.
        bool known = true;
        int samples = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(start, end) * 4), 1, 2048);
        for (int i = 1; i <= samples && known; i++)
            known = FogManager.Instance == null || FogManager.Instance.IsKnownForMovement(Vector3.Lerp(from, to, i / (float)samples));
        return MovementRisk.RequiresReview(start, end, known, clear);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void MovementNoticeRpc(string message, RpcParams rpcParams = default) => DiceUI.Instance?.ShowToolNotice(message);

    [Rpc(SendTo.SpecifiedInParams)]
    private void MovementPromptRpc(int request, bool approval, RpcParams rpcParams = default) =>
        MovementReviewUI.Show(this, request, approval);

    public void AnswerMovement(int request, bool accept)
    {
        if (IsSpawned) AnswerMovementRpc(request, accept);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AnswerMovementRpc(int request, bool accept, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!_movementPending || request != _movementRequestId) return;
        bool master = NetworkPermissions.IsHostClient(sender);
        if (!master && (sender != _movementClient || accept && _movementPolicy == PlayerMovementPolicy.Approval)) return;
        bool valid = Time.unscaledTimeAsDouble < _movementDeadline && CanClientStartDrag(_movementClient)
            && !IsLocalDragActiveAny() && (CommittedPosition - _movementFrom).sqrMagnitude < 0.0001f;
        // Changing the policy invalidates old authorizations.
        valid &= SceneFileStore.MovementPolicy == _movementPolicy;
        var grid = FindAnyObjectByType<GridManager>();
        var cell = grid == null ? new Vector2Int(-1, -1) : grid.GetGridPosition(_movementTo);
        valid &= cell.x >= 0 && (cell == _currentCell || !grid.IsCellOccupied(cell));
        if (accept && valid)
        {
            Vector3 previous = _movementFrom;
            string id = SceneId; var memory = FogManager.Instance?.Capture(true);
            GameMasterUndo.Record("разрешённое перемещение", () => { FindSceneToken(id)?.RestorePosition(previous); if (memory != null) FogManager.Instance?.Restore(memory, false); });
            transform.position = _movementTo;
            SnapAuthoritative();
            RestorePosition(transform.position);
            ClearMovementRequest("Перемещение выполнено.");
        }
        else ClearMovementRequest(accept ? "Ситуация изменилась. Повторите перемещение." : "Перемещение отменено; токен остался на месте.");
    }

    public void ResolveMovementAsMaster(bool accept) => AnswerMovement(_movementRequestId, accept);

    private void UpdateMovementRequest()
    {
        if (IsSpawned && IsServer && _netMovementPolicy.Value != (int)SceneFileStore.MovementPolicy)
            _netMovementPolicy.Value = (int)SceneFileStore.MovementPolicy;
        if (!IsServer || !_movementPending) return;
        if (Time.unscaledTimeAsDouble >= _movementDeadline || !NetworkManager.ConnectedClients.ContainsKey(_movementClient)
            || !CanClientStartDrag(_movementClient) || SceneFileStore.MovementPolicy != _movementPolicy
            || (CommittedPosition - _movementFrom).sqrMagnitude > 0.0001f)
            ClearMovementRequest("Ожидание завершено; токен остался на месте. Можно двигаться снова.");
    }

    private void ClearMovementRequest(string message)
    {
        if (!_movementPending) return;
        _movementPending = false;
        if (IsSpawned && NetworkManager.ConnectedClients.ContainsKey(_movementClient))
            MovementResultRpc(_movementRequestId, message, RpcTarget.Single(_movementClient, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void MovementResultRpc(int request, string message, RpcParams rpcParams = default)
    {
        MovementReviewUI.Complete(this, request);
        DiceUI.Instance?.ShowToolNotice(message);
    }
}
