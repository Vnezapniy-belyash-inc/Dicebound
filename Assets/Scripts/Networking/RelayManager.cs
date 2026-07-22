using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Интеграция с Unity Relay: аллокация для хоста, подключение для клиента.
/// API NGO 2.13+ — без RelayServerData, через SetHostRelayData/SetClientRelayData.
/// </summary>
public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    /// <summary>Актуальный join-код текущей сессии (хост и клиенты).</summary>
    public static string CurrentJoinCode { get; private set; }

    public static void ClearJoinCode() => CurrentJoinCode = null;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Инициализирует Unity Services (один раз при старте приложения).</summary>
    public static async Task InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
            return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[Relay] Signed in anonymously. PlayerId: {AuthenticationService.Instance.PlayerId}");
        }
    }

    /// <summary>
    /// Хост: создать Relay-аллокацию и вернуть join-код для клиентов.
    /// </summary>
    /// <param name="maxPlayers">Максимум игроков (включая хоста).</param>
    /// <returns>Join-код (строка из 6 символов).</returns>
    public async Task<string> CreateRelayAllocation(int maxPlayers = 9)
    {
        await InitializeServices();
        ResetTransport();

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetHostRelayData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData
        );

        Debug.Log($"[Relay] Host allocation created. Join code: {joinCode}");
        CurrentJoinCode = joinCode;
        return joinCode;
    }

    /// <summary>
    /// Клиент: подключиться к Relay-аллокации по join-коду (с повторами при сбое HTTP).
    /// </summary>
    public async Task JoinRelayAllocation(string joinCode, int maxAttempts = 3)
    {
        await InitializeServices();
        ResetTransport();

        Exception lastError = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    Debug.LogWarning($"[Relay] Join retry {attempt}/{maxAttempts}...");
                    await Task.Delay(350 * attempt);
                    ResetTransport();
                }

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                ApplyClientRelayData(joinAllocation);
                Debug.Log($"[Relay] Joined relay with code: {joinCode}");
                CurrentJoinCode = joinCode;
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.LogWarning($"[Relay] Join attempt {attempt} failed: {ex.Message}");
            }
        }

        throw lastError ?? new InvalidOperationException("Relay join failed.");
    }

    private static void ResetTransport()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777);
    }

    private static void ApplyClientRelayData(JoinAllocation joinAllocation)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetClientRelayData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.Key,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData
        );
    }
}
