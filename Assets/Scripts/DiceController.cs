using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls a D20 dice: rolling via click on the die, detecting when it stops,
/// and reporting the top face value via a UI Text component.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DiceMeshGenerator))]
public class DiceController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("UI Text element to display the rolled value")]
    public Text resultText;

    [Tooltip("Camera used for click detection. Falls back to Camera.main.")]
    public Camera raycastCamera;

    [Header("Physics")]
    [Tooltip("Base force applied when rolling")]
    public float throwForce = 5f;

    [Tooltip("Base torque applied when rolling")]
    public float throwTorque = 5f;

    [Tooltip("Minimum upward impulse so the die lifts off the surface")]
    public float minUpwardForce = 1f;

    public event Action<int> OnRolled;

    private Rigidbody rb;
    private Vector3[] faceNormals;
    private int[] faceValues;
    private bool wasSleeping = true;
    private bool hasThrown;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.Sleep();
    }

    private void Start()
    {
        DiceMeshGenerator meshGenerator = GetComponent<DiceMeshGenerator>();
        faceNormals = meshGenerator.FaceNormals;
        faceValues = meshGenerator.FaceValues;

        if (raycastCamera == null)
            raycastCamera = Camera.main;
    }

    private void Update()
    {
        if (TryGetDiceClick())
            Roll();

        bool sleeping = rb.IsSleeping();
        if (hasThrown && !wasSleeping && sleeping)
            ShowResult();

        wasSleeping = sleeping;
    }

    bool TryGetDiceClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return false;

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
            return false;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit))
            return false;

        return hit.rigidbody == rb;
    }

    /// <summary>
    /// Applies a random impulse and torque to simulate a roll.
    /// </summary>
    public void Roll()
    {
        hasThrown = true;
        rb.WakeUp();

        Vector3 force = UnityEngine.Random.onUnitSphere * throwForce;
        force.y = Mathf.Max(force.y, minUpwardForce);

        Vector3 torque = UnityEngine.Random.onUnitSphere * throwTorque;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        if (resultText != null)
            resultText.text = "";
    }

    void ShowResult()
    {
        int bestIndex = 0;
        float bestDot = -1f;

        for (int i = 0; i < faceNormals.Length; i++)
        {
            Vector3 worldNormal = transform.TransformDirection(faceNormals[i]);
            float dot = Vector3.Dot(worldNormal, Vector3.up);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }

        int value = faceValues[bestIndex];
        OnRolled?.Invoke(value);

        if (resultText != null)
            resultText.text = $"Выпало: {value}";
        else
            Debug.Log($"Dice result: {value}");
    }
}
