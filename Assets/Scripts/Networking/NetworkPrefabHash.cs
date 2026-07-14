using System.Reflection;
using Unity.Netcode;

/// <summary>
/// Forces a stable GlobalObjectIdHash on runtime-created network prefabs.
/// </summary>
internal static class NetworkPrefabHash
{
    public static void Set(NetworkObject netObj, uint hash)
    {
        if (netObj == null) return;

        var prop = typeof(NetworkObject).GetProperty(
            "GlobalObjectIdHash",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(netObj, hash);
            return;
        }

        var field = typeof(NetworkObject).GetField(
            "GlobalObjectIdHash",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(netObj, hash);
    }
}
