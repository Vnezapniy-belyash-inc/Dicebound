using UnityEngine;

/// <summary>Данные одной грани дайса: позиция центра, нормаль наружу, значение.</summary>
public struct DieFaceData
{
    public Vector3 center;   // локальные координаты центра грани
    public Vector3 normal;   // локальная нормаль (наружу)
    public int value;        // значение на грани
}
