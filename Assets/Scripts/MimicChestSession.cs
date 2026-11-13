using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Server owns wallets and rolls. Public skin state contains no balances.
public sealed class MimicChestSession : MonoBehaviour
{
    public static MimicChestSession Instance;
    public event Action<int[,]> Rolled;
    public event Action SpinRejected;
    public readonly Dictionary<ulong, int> Balances = new();
    public readonly Dictionary<ulong, int> Skins = new();
    readonly Dictionary<string, (int balance, int skin)> accounts = new();
    readonly Dictionary<ulong, float> cooldowns = new();
    NetworkManager network;
    CustomMessagingManager messaging;
    float nextSync;
    readonly System.Random random = new();
    const string Request = "Mimic.Request", State = "Mimic.State", Result = "Mimic.Result";
    public int Balance => network != null && Balances.TryGetValue(network.LocalClientId, out int value) ? value : 0;
    public bool IsMaster => network != null && network.IsServer;
    void Awake() => Instance = this;
    void Update()
    {
        var current = NetworkManager.Singleton;
        if (current == null || !current.IsListening) { Unbind(); return; }
        if (network != current || messaging != current.CustomMessagingManager)
        {
            Unbind(); network = current; messaging = current.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(Request, OnRequest);
            messaging.RegisterNamedMessageHandler(State, OnState);
            messaging.RegisterNamedMessageHandler(Result, OnResult);
        }
        if (network.IsServer && Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 1;
            foreach (var id in network.ConnectedClientsIds) Ensure(id);
            Sync();
        }
    }
    void Unbind()
    {
        if (messaging != null) { messaging.UnregisterNamedMessageHandler(Request); messaging.UnregisterNamedMessageHandler(State); messaging.UnregisterNamedMessageHandler(Result); }
        messaging = null; network = null; Balances.Clear(); Skins.Clear(); cooldowns.Clear();
    }
    void OnDestroy() { Unbind(); if (Instance == this) Instance = null; }
    string Key(ulong id) => PlayerColors.GetNickname(id) ?? (id == NetworkManager.ServerClientId ? "master" : null);
    bool Ensure(ulong id)
    {
        var key = Key(id); if (key == null) return false;
        if (!accounts.TryGetValue(key, out var account)) accounts[key] = account = (Mathf.Clamp(PlayerPrefs.GetInt("Mimic.Balance." + key, MimicChestRules.StartingBalance), 0, 1000000000), Mathf.Clamp(PlayerPrefs.GetInt("Mimic.Skin." + key, 0), 0, 4));
        Balances[id] = account.balance; Skins[id] = account.skin; return true;
    }
    void Save(ulong id) { var key = Key(id); if (key != null) { accounts[key] = (Balances[id], Skins[id]); PlayerPrefs.SetInt("Mimic.Balance." + key, Balances[id]); PlayerPrefs.SetInt("Mimic.Skin." + key, Skins[id]); PlayerPrefs.Save(); } }
    public void Grant(ulong id, int amount)
    {
        if (!IsMaster || amount <= 0 || amount > 100000 || !network.ConnectedClients.ContainsKey(id) || !Ensure(id)) return;
        Balances[id] = (int)Math.Min(1000000000L, (long)Balances[id] + amount); Save(id); Sync();
    }
    public bool Spin()
    {
        if (messaging == null || Balance < MimicChestRules.SpinCost) return false;
        if (IsMaster) Roll(network.LocalClientId);
        else { using var writer = new FastBufferWriter(1, Allocator.Temp); messaging.SendNamedMessage(Request, NetworkManager.ServerClientId, writer); }
        return true;
    }
    void OnRequest(ulong sender, FastBufferReader reader) { if (IsMaster) Roll(sender); }
    void Roll(ulong id)
    {
        if (!network.ConnectedClients.ContainsKey(id) || !Ensure(id)) { Reject(id); return; }
        if (cooldowns.TryGetValue(id, out float until) && Time.unscaledTime < until) { Reject(id); return; }
        if (Balances[id] < MimicChestRules.SpinCost) { Reject(id); return; }
        cooldowns[id] = Time.unscaledTime + 1.3f;
        var grid = MimicChestRules.Roll(random);
        Balances[id] += MimicChestRules.Evaluate(grid, new bool[3,5]) - MimicChestRules.SpinCost;
        Skins[id] = Math.Max(Skins[id], MimicChestRules.EvaluateSkin(grid)); Save(id); Sync();
        if (id == network.LocalClientId) Rolled?.Invoke(grid);
        else
        {
            using var writer = new FastBufferWriter(60, Allocator.Temp);
            foreach (int symbol in grid) writer.WriteValueSafe(symbol);
            messaging.SendNamedMessage(Result, id, writer);
        }
    }
    void Reject(ulong id)
    {
        if (id == network.LocalClientId) { SpinRejected?.Invoke(); return; }
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe(-1);
        messaging.SendNamedMessage(Result, id, writer);
    }
    void OnResult(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        if (reader.Length == 4) { SpinRejected?.Invoke(); return; }
        if (!reader.TryBeginRead(60)) return;
        var grid = new int[3,5];
        for(int r=0;r<3;r++) for(int c=0;c<5;c++) reader.ReadValueSafe(out grid[r,c]);
        Rolled?.Invoke(grid);
    }
    void Sync()
    {
        foreach (var recipient in network.ConnectedClientsIds)
        {
            if (recipient == network.LocalClientId) continue;
            using var writer = new FastBufferWriter(4 + network.ConnectedClientsIds.Count * 16, Allocator.Temp);
            writer.WriteValueSafe(network.ConnectedClientsIds.Count);
            foreach (var id in network.ConnectedClientsIds)
            {
                writer.WriteValueSafe(id); writer.WriteValueSafe(Skins.TryGetValue(id, out int skin) ? skin : 0);
                writer.WriteValueSafe(id == recipient && Balances.TryGetValue(id, out int balance) ? balance : -1);
            }
            messaging.SendNamedMessage(State, recipient, writer);
        }
    }
    void OnState(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || IsMaster || !reader.TryBeginRead(4)) return;
        reader.ReadValueSafe(out int count);
        if (count < 0 || count > 1024 || !reader.TryBeginRead(count * 16)) return;
        Balances.Clear(); Skins.Clear();
        for(int i=0;i<count;i++) { reader.ReadValueSafe(out ulong id); reader.ReadValueSafe(out int skin); reader.ReadValueSafe(out int balance); Skins[id]=skin; if(balance>=0) Balances[id]=balance; }
    }
}
