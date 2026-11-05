/// <summary>Shared bounds for runtime, saved scenes and client snapshots.</summary>
public static class TabletopLimits
{
    public const int MaxGridDimension = 500;
    public const int DefaultGridDimension = 500;
    public const float MaxMapScale = 50f;
    // Two bit-packed 4x4-per-cell masks, plus compression overhead.
    public const int MaxFogStateBytes = MaxGridDimension * MaxGridDimension * 4 + 65536;
}
