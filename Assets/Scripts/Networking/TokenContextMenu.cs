using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Контекстное меню токена через OnGUI. Никаких Canvas/кнопок/EventSystem.
/// ПКМ по токену → окно с кнопкой «Загрузить изображение».
/// </summary>
public class TokenContextMenu : MonoBehaviour
{
    private TokenController _targetToken;
    private Camera _cam;
    private bool _menuOpen;
    private Rect _menuRect;

    private void Start()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        Mouse m = Mouse.current;
        if (m == null || _cam == null) return;

        // ПКМ по токену — показать меню
        if (m.rightButton.wasPressedThisFrame)
        {
            Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var token = hit.collider.GetComponentInParent<TokenController>();
                if (token != null)
                {
                    _targetToken = token;
                    _menuOpen = true;
                    Vector2 pos = m.position.ReadValue();
                    _menuRect = new Rect(pos.x + 5, Screen.height - pos.y - 5, 180, 30);
                    return;
                }
            }
            _menuOpen = false;
            _targetToken = null;
        }

        // ЛКМ мимо меню — закрыть (не закрываем если клик по меню!)
        if (m.leftButton.wasPressedThisFrame && _menuOpen)
        {
            Vector2 guiPos = new Vector2(m.position.ReadValue().x, Screen.height - m.position.ReadValue().y);
            if (!_menuRect.Contains(guiPos))
            {
                _menuOpen = false;
                _targetToken = null;
            }
        }
    }

    private void OnGUI()
    {
        if (!_menuOpen || _targetToken == null) return;

        GUI.Box(_menuRect, "");
        if (GUI.Button(new Rect(_menuRect.x + 5, _menuRect.y + 5, _menuRect.width - 10, 20),
            "Загрузить изображение"))
        {
            _menuOpen = false;
            LoadImage();
        }
    }

    void LoadImage()
    {
        if (_targetToken == null || !_targetToken.IsOwner) return;

#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Изображение для токена", "", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(path)) return;
        byte[] data = System.IO.File.ReadAllBytes(path);

        Texture2D temp = new Texture2D(2, 2);
        temp.LoadImage(data);
        byte[] jpg = temp.EncodeToJPG(20); // меньше 1KB для RPC
        Destroy(temp);

        _targetToken.LoadImage(jpg);
#else
        Debug.LogWarning("Загрузка только в Editor");
#endif
    }
}
