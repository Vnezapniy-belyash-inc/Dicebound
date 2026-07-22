using System;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public struct MeasurementSnapshot : INetworkSerializable, IEquatable<MeasurementSnapshot>
{
    public ulong ClientId;
    public byte Mode;
    public byte ActiveFlag;
    public byte PointsReadyFlag;
    public Vector3 PointA;
    public Vector3 PointB;
    public Vector3 ColorRgb;

    public bool Active => ActiveFlag != 0;
    public bool HasPoints => PointsReadyFlag != 0;

    public static MeasurementSnapshot Create(ulong clientId, int mode, Color color)
    {
        var rgb = new Vector3(color.r, color.g, color.b);
        return new MeasurementSnapshot
        {
            ClientId = clientId,
            Mode = (byte)mode,
            ActiveFlag = 1,
            PointsReadyFlag = 0,
            PointA = Vector3.zero,
            PointB = Vector3.zero,
            ColorRgb = rgb
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Mode);
        serializer.SerializeValue(ref ActiveFlag);
        serializer.SerializeValue(ref PointsReadyFlag);
        serializer.SerializeValue(ref PointA);
        serializer.SerializeValue(ref PointB);
        serializer.SerializeValue(ref ColorRgb);
    }

    public bool Equals(MeasurementSnapshot other) =>
        ClientId == other.ClientId
        && Mode == other.Mode
        && ActiveFlag == other.ActiveFlag
        && PointsReadyFlag == other.PointsReadyFlag
        && PointA == other.PointA
        && PointB == other.PointB
        && ColorRgb == other.ColorRgb;

    public override bool Equals(object obj) => obj is MeasurementSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ClientId.GetHashCode();
            hash = (hash * 397) ^ Mode;
            hash = (hash * 397) ^ ActiveFlag;
            hash = (hash * 397) ^ PointsReadyFlag;
            hash = (hash * 397) ^ PointA.GetHashCode();
            hash = (hash * 397) ^ PointB.GetHashCode();
            hash = (hash * 397) ^ ColorRgb.GetHashCode();
            return hash;
        }
    }
}
