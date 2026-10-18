using UnityEngine;

public static class LocalControls
{
    public static float CameraSpeed { get; private set; } = 1;
    public static bool TokenHover { get; private set; } = true;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Load()
    {
        float stored = PlayerPrefs.GetFloat("Controls.CameraSpeed", 1);
        CameraSpeed = float.IsNaN(stored) || float.IsInfinity(stored) ? 1 : Mathf.Clamp(stored, 0.5f, 2f);
        TokenHover = PlayerPrefs.GetInt("Controls.TokenHover", 1) != 0;
    }
    public static void SetCameraSpeed(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        CameraSpeed = Mathf.Clamp(value, 0.5f, 2f);
        PlayerPrefs.SetFloat("Controls.CameraSpeed", CameraSpeed); PlayerPrefs.Save();
    }
    public static void SetTokenHover(bool value)
    {
        TokenHover = value; PlayerPrefs.SetInt("Controls.TokenHover", value ? 1 : 0); PlayerPrefs.Save();
    }
}
