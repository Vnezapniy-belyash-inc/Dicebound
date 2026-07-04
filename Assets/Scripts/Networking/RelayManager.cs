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
        return joinCode;
    }

    /// <summary>
    /// Клиент: подключиться к Relay-аллокации по join-коду.
    /// </summary>
    /// <param name="joinCode">6-символьный код от хоста.</param>
    public async Task JoinRelayAllocation(string joinCode)
    {
        await InitializeServices();

        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetClientRelayData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.Key,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData
        );

        Debug.Log($"[Relay] Joined relay with code: {joinCode}");
    }
}
