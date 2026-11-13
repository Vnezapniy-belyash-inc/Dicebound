using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class MimicChestWindow
{
    private RectTransform _helpPanel, _helpButton;
    private bool _helpPinned;
    private float _helpLeaveAt;
    private Texture2D _helpCircleTexture;
    private Sprite _helpCircleSprite;
    private static readonly Color HelpGold = new Color32(226, 188, 116, 255);

    private void BuildHelp(Transform chest)
    {
        // A small antialiased medallion, independent of the cabinet image's alpha.
        _helpCircleTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        _helpCircleTexture.name = "MimicHelpCircle";
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32));
                _helpCircleTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(32 - distance)));
            }
        _helpCircleTexture.Apply();
        _helpCircleSprite = Sprite.Create(_helpCircleTexture, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
        var button = AddButton(chest, "RulesHelp", 1040, 950, 72, 72, HelpGold);
        _helpButton = (RectTransform)button.transform;
        button.GetComponent<UnityEngine.UI.Image>().sprite = _helpCircleSprite;
        var inset = AddRect(button.transform, "Inset", 3, 3, 66, 66).gameObject.AddComponent<UnityEngine.UI.Image>();
        inset.sprite = _helpCircleSprite;
        inset.color = new Color32(41, 26, 18, 255);
        inset.raycastTarget = false;
        var question = AddText(button.transform, "Question", 0, -2, 72, 72, "?", 48, new Color32(255, 232, 182, 180));
        question.raycastTarget = false;
        button.onClick.AddListener(() => { _helpPinned = !_helpPinned; _helpPanel.gameObject.SetActive(true); });

        _helpPanel = AddRect(chest, "CasinoRules", 215, 175, 910, 760);
        _helpPanel.gameObject.AddComponent<UnityEngine.UI.Image>().color = HelpGold;
        var inner = AddRect(_helpPanel, "Paper", 2, 2, 906, 756);
        inner.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color32(30, 23, 20, 255);
        HelpText("СЕКРЕТЫ СУНДУКА", 22, 14, 866, 42, 30, HelpGold);
        HelpText("РЕДКОСТЬ СИМВОЛОВ", 22, 66, 866, 30, 22, HelpGold);
        HelpText("Чем длиннее шкала, тем реже символ", 22, 96, 866, 26, 18, new Color32(188, 177, 158, 255));
        string[] names = { "Факел", "Щит", "Меч", "Зелье", "Свиток", "Ключ", "Корона", "d20" };
        for (int i = 0; i < 8; i++)
        {
            float x = 27 + i * 108;
            AddImage(_helpPanel, "Symbol", x + 12, 132, 70, 70, _symbolTextures[i]);
            HelpText(names[i], x - 3, 205, 106, 26, 18, Color.white);
            for (int bar = 0; bar < 8; bar++)
                AddImage(_helpPanel, "Rarity", x + bar * 12, 240, 8, 8).color = bar <= i ? HelpGold : new Color32(74, 61, 44, 255);
        }
        HelpText("Чаще", 22, 258, 170, 24, 18, HelpGold);
        HelpText("Реже", 718, 258, 170, 24, 18, HelpGold);
        HelpText("КОМБИНАЦИИ → СКИНЫ", 22, 299, 866, 32, 23, HelpGold);
        SkinRow(342, 0, 4, "БРОНЗА", "Факелы · любая горизонталь", new Color32(206, 132, 74, 255));
        SkinRow(420, 1, 4, "СЕРЕБРО", "Щит / меч / зелье / свиток / ключ", new Color32(200, 216, 233, 255));
        SkinRow(498, 6, 4, "ЗОЛОТО", "Короны или d20 · любая горизонталь", new Color32(255, 203, 87, 255));
        SkinRow(576, 7, 5, "АЛМАЗ", "Любой символ · только центральная линия", new Color32(135, 235, 255, 255));
        HelpText("Одинаковые подряд. Для серебра — тоже любая горизонталь.", 22, 654, 866, 26, 18, Color.white);
        HelpText("Дерево — сразу. Лучший открытый скин применяется автоматически.", 22, 681, 866, 26, 18, HelpGold);
        HelpText("Нажмите ? — закрепить / открепить подсказку", 22, 720, 866, 24, 17, new Color32(188, 177, 158, 255));
        _helpPanel.gameObject.SetActive(false);
    }

    private void HelpText(string value, float x, float y, float width, float height, int size, Color color)
    {
        var text = AddText(_helpPanel, "HelpText", x, y, width, height, value, size, color);
        text.raycastTarget = false;
    }

    private void SkinRow(float y, int symbol, int count, string reward, string description, Color color)
    {
        AddImage(_helpPanel, "RewardRow", 20, y, 870, 70).color = new Color32(49, 38, 29, 255);
        for (int i = 0; i < count; i++)
        {
            AddImage(_helpPanel, "MatchCell", 32 + i * 51, y + 10, 46, 46).color = color * new Color(1, 1, 1, .22f);
            AddImage(_helpPanel, "MatchSymbol", 35 + i * 51, y + 13, 40, 40, _symbolTextures[symbol]);
        }
        HelpText("→", 296, y + 8, 45, 48, 30, color);
        HelpText(reward, 350, y + 3, 520, 30, 23, color);
        HelpText(description, 350, y + 35, 520, 29, 18, Color.white);
    }

    private void RefreshHelp()
    {
        if (_helpPanel == null || _modal == null || !_modal.activeInHierarchy) return;
        bool hovering = false;
        if (Mouse.current != null)
        {
            Vector2 point = Mouse.current.position.ReadValue();
            hovering = RectTransformUtility.RectangleContainsScreenPoint(_helpButton, point) ||
                (_helpPanel.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_helpPanel, point));
        }
        if (hovering) _helpLeaveAt = Time.unscaledTime + .22f;
        _helpPanel.gameObject.SetActive(_helpPinned || hovering || Time.unscaledTime < _helpLeaveAt);
    }

    private void OnDestroy()
    {
        if (_helpCircleSprite != null) Destroy(_helpCircleSprite);
        if (_helpCircleTexture != null) Destroy(_helpCircleTexture);
        if (MimicChestSession.Instance != null) { MimicChestSession.Instance.Rolled -= ReceiveRoll; MimicChestSession.Instance.SpinRejected -= RejectSpin; }
    }
}
