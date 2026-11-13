using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;
using RawImage = UnityEngine.UI.RawImage;
using Text = UnityEngine.UI.Text;

/// <summary>Local easter egg, opened by typing its code outside text fields.</summary>
public sealed partial class MimicChestWindow : MonoBehaviour
{
    private const string ResourcePath = "UI/MimicChest/";
    private const string RussianCode = "БУРМАЛДА";
    private const string LatinCode = "BURMALDA";
    private const int Rows = MimicChestRules.Rows;
    private const int Columns = MimicChestRules.Columns;
    private const int CellSize = 196;
    private const int SymbolSize = 164;
    private const int ReelX = 180;
    private const int ReelY = 290;
    private const int SymbolInset = (CellSize - SymbolSize) / 2;
    private const int ReelInteriorInset = 17;
    private const int ReelInteriorWidth = CellSize - 2 * ReelInteriorInset;

    private readonly System.Random _random = new();
    private readonly RectTransform[,] _beltCells = new RectTransform[Rows + 1, Columns];
    private readonly RawImage[,] _symbols = new RawImage[Rows + 1, Columns];
    private readonly RawImage[,] _winFrames = new RawImage[Rows, Columns];
    private readonly bool[,] _winningCells = new bool[Rows, Columns];
    private Keyboard _keyboard;
    private GameObject _modal;
    private RawImage _leverReady, _leverPulled;
    private Button _spinButton;
    private Text _balanceLabel;
    private RectTransform _balance, _payout;
    private CanvasGroup _payoutOpacity;
    private Texture2D[] _symbolTextures, _glyphTextures;
    private int[,] _grid;
    private int _balanceValue = 0;
    private int _russianPosition, _latinPosition;
    private bool _spinning;
    private Coroutine _rewardAnimation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var root = new GameObject("MimicChest");
        DontDestroyOnLoad(root);
        root.AddComponent<MimicChestSession>();
        root.AddComponent<MimicChestWindow>();
    }

    private void Start()
    {
        MimicChestSession.Instance.Rolled += ReceiveRoll;
        MimicChestSession.Instance.SpinRejected += RejectSpin;
    }
    private void RejectSpin() { _requestDeadline = 0; _spinning = false; if (_spinButton != null) RefreshBalance(); }
    private UnityEngine.UI.Outline _leverOutline;

    private float _requestDeadline;
    private void ReceiveRoll(int[,] result) { _spinning = true; StartCoroutine(Spin(result)); }

    private void Update()
    {
        if (_leverOutline != null)
        {
            _leverOutline.enabled = _spinButton.interactable;
            _leverOutline.effectColor = new Color(1f, .78f, .04f, Mathf.Lerp(.12f, 1f, Mathf.Pow(.5f + .5f * Mathf.Sin(Time.unscaledTime * 5f), .65f)));
        }
        RefreshHelp();
        if (_keyboard != Keyboard.current)
        {
            DetachKeyboard();
            _keyboard = Keyboard.current;
            if (_keyboard != null) _keyboard.onTextInput += ReadCode;
        }
        if (!GameplayInputGate.AllowsKeyboardHotkeys) ResetCode();
        if (_balance != null && !_spinning && _balanceValue != MimicChestSession.Instance.Balance)
        { _balanceValue = MimicChestSession.Instance.Balance; RefreshBalance(); }
        if (_spinning && _requestDeadline > 0 && Time.unscaledTime > _requestDeadline)
        { _requestDeadline = 0; _spinning = false; RefreshBalance(); }

    }

    private void OnDisable() => DetachKeyboard();

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) ResetCode();
    }

    private void DetachKeyboard()
    {
        if (_keyboard != null) _keyboard.onTextInput -= ReadCode;
        _keyboard = null;
        ResetCode();
    }

    private void ResetCode() => _russianPosition = _latinPosition = 0;

    private void ReadCode(char character)
    {
        if (!GameplayInputGate.AllowsKeyboardHotkeys || _keyboard == null ||
            _keyboard.ctrlKey.isPressed || _keyboard.altKey.isPressed || _keyboard.leftMetaKey.isPressed ||
            _keyboard.rightMetaKey.isPressed)
        {
            ResetCode();
            return;
        }

        character = char.ToUpperInvariant(character);
        bool russian = AdvanceCode(character, RussianCode, ref _russianPosition);
        bool latin = AdvanceCode(character, LatinCode, ref _latinPosition);
        if (!russian && !latin) return;

        ResetCode();
        if (_modal == null) BuildWindow();
        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        _modal.SetActive(true);
    }

    private static bool AdvanceCode(char character, string code, ref int position)
    {
        position = character == code[position] ? position + 1 : character == code[0] ? 1 : 0;
        return position == code.Length;
    }

    private void BuildWindow()
    {
        var screen = new GameObject("MimicChestCanvas", typeof(Canvas), typeof(CanvasScaler));
        screen.transform.SetParent(transform, false);
        screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = screen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1460, 1280);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var chest = new GameObject("Chest", typeof(RectTransform)).GetComponent<RectTransform>();
        chest.SetParent(screen.transform, false);
        chest.anchorMin = chest.anchorMax = chest.pivot = new Vector2(0.5f, 0.5f);
        chest.sizeDelta = new Vector2(1340, 1174);
        AddImage(chest, "ChestInterior", 175, 280, 990, 610).color = new Color32(24, 13, 9, 255);
        AddImage(chest, "Cabinet", 0, 0, 1340, 1174, Load("machine-background"));

        _symbolTextures = System.Array.ConvertAll(MimicChestRules.SymbolNames, name => Load("ReelSymbols/" + name));
        _glyphTextures = new Texture2D[11];
        for (int i = 0; i < 10; i++) _glyphTextures[i] = Load("RewardGlyphs/" + i);
        _glyphTextures[10] = Load("RewardGlyphs/plus");

        var reel = Load("reel-frame");
        var highlight = Load("winning-cell-frame");
        for (int column = 0; column < Columns; column++)
        {
            int x = ReelX + column * CellSize;
            AddImage(chest, "Reel", x, ReelY, CellSize, Rows * CellSize, reel);
            var clip = AddRect(chest, "ReelInterior", x + ReelInteriorInset, ReelY + ReelInteriorInset,
                ReelInteriorWidth, Rows * CellSize - 2 * ReelInteriorInset);
            clip.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i <= Rows; i++)
            {
                var cell = AddImage(clip, "BeltCell", 0, 0, ReelInteriorWidth, CellSize, reel);
                // Repeat the middle section of the reel artwork, including its top divider.
                cell.uvRect = new Rect(0.09f, 0.34f, 0.82f, 0.32f);
                _beltCells[i, column] = cell.rectTransform;
                _symbols[i, column] = AddImage(cell.transform, "Symbol", SymbolInset - ReelInteriorInset,
                    SymbolInset, SymbolSize, SymbolSize);
            }
            for (int row = 0; row < Rows; row++)
            {
                _winFrames[row, column] = AddImage(chest, "WinningCell", x - 1,
                    ReelY + row * CellSize - 4, 198, 204, highlight);
                _winFrames[row, column].enabled = false;
            }
        }

        _leverReady = AddImage(chest, "LeverRaised", 1121, 404, 226, 452, Load("pull-lever-front-ready"));
        _leverPulled = AddImage(chest, "LeverPulled", 1146, 658, 176, 191, Load("pull-lever-front-pulled"));
        _leverPulled.enabled = false;
        _leverOutline = _leverReady.gameObject.AddComponent<UnityEngine.UI.Outline>();
        _leverOutline.effectDistance = new Vector2(8, -8);
        _leverOutline.useGraphicAlpha = true;
        _spinButton = AddButton(chest, "PullLever", 1121, 404, 219, 452, Color.clear);
        _spinButton.onClick.AddListener(StartSpin);

        _balanceLabel = AddText(chest, "BalanceLabel", 415, 963, 510, 40, "ЖЕТОНЫ", 26,
            new Color32(60, 26, 10, 255));
        _balance = AddRect(chest, "Balance", 670, 998, 0, 0);
        _payout = AddRect(chest, "Payout", 0, 0, 0, 0);
        _payoutOpacity = _payout.gameObject.AddComponent<CanvasGroup>();
        _payout.gameObject.SetActive(false);

        var close = AddButton(chest, "Close", 1282, 10, 54, 54, new Color32(67, 28, 21, 255));
        AddText(close.transform, "CloseLabel", 0, 0, 54, 54, "×", 44, new Color32(255, 225, 160, 255));
        _modal = UiModalScope.Wrap(chest.gameObject, close);
        _modal.GetComponent<UiModalScope>().dismiss = Close;
        close.onClick.AddListener(Close);

        BuildHelp(chest);
        _grid = MimicChestRules.Roll(_random);
        ShowGrid();
        RefreshBalance();
    }

    private void Close()
    {
        _helpPinned = false;
        if (_helpPanel != null) _helpPanel.gameObject.SetActive(false);
        _modal.SetActive(false);
        ResetCode();
    }

    private void StartSpin()
    {
        if (_spinning || _balanceValue < MimicChestRules.SpinCost) return;
        _requestDeadline = Time.unscaledTime + 8;
        _spinning = true;
        if (!MimicChestSession.Instance.Spin()) _spinning = false;
        RefreshBalance();
    }

    private IEnumerator Spin(int[,] result)
    {
        _requestDeadline = 0;
        foreach (var frame in _winFrames) frame.enabled = false;
        if (_rewardAnimation != null) StopCoroutine(_rewardAnimation);
        _payout.gameObject.SetActive(false);
        var belts = new int[Columns][];
        for (int column = 0; column < Columns; column++)
        {
            int steps = 7 + column * 2;
            var belt = new int[steps + Rows + 1];
            for (int i = 0; i < belt.Length; i++) belt[i] = _random.Next(_symbolTextures.Length);
            for (int row = 0; row < Rows; row++)
            {
                belt[row + 1] = result[row, column];
                belt[steps + row + 1] = _grid[row, column];
            }
            belts[column] = belt;
        }

        float elapsed = 0;
        const float firstStop = 0.72f;
        const float stopInterval = 0.17f;
        const float leverHold = 0.23f;
        while (elapsed < firstStop + (Columns - 1) * stopInterval)
        {
            _leverReady.enabled = elapsed >= leverHold;
            _leverPulled.enabled = elapsed < leverHold;
            for (int column = 0; column < Columns; column++)
            {
                float progress = Mathf.Clamp01(elapsed / (firstStop + column * stopInterval));
                DrawBelt(column, belts[column], progress);
            }
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        _grid = result;
        ShowGrid();
        int payout = MimicChestRules.Evaluate(result, _winningCells);
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
                _winFrames[row, column].enabled = _winningCells[row, column];

        _balanceValue = MimicChestSession.Instance.Balance;
        _spinning = false;
        _leverReady.enabled = true;
        _leverPulled.enabled = false;
        RefreshBalance();
        if (payout > 0 && _modal.activeSelf) _rewardAnimation = StartCoroutine(FloatReward(payout));
    }

    private void DrawBelt(int column, int[] belt, float progress)
    {
        int steps = belt.Length - Rows - 1;
        float remaining = 1 - progress;
        float distance = steps * (1 - remaining * remaining);
        int travelled = Mathf.FloorToInt(distance);
        float shift = (distance - travelled) * CellSize;
        float stretch = 20 * remaining;
        for (int i = 0; i <= Rows; i++)
        {
            var cell = _beltCells[i, column];
            cell.anchoredPosition = new Vector2(0, ReelInteriorInset - (i - 1) * CellSize - shift);
            var symbol = _symbols[i, column];
            symbol.texture = _symbolTextures[belt[steps - travelled + i]];
            Place(symbol.rectTransform, SymbolInset - ReelInteriorInset, SymbolInset - stretch / 2,
                SymbolSize, SymbolSize + stretch);
            symbol.color = new Color(1, 1, 1, 1 - 0.20f * remaining);
        }
    }

    private void ShowGrid()
    {
        for (int column = 0; column < Columns; column++)
        {
            _beltCells[0, column].anchoredPosition = new Vector2(0, ReelInteriorInset + CellSize);
            for (int row = 0; row < Rows; row++)
            {
                _beltCells[row + 1, column].anchoredPosition = new Vector2(0, ReelInteriorInset - row * CellSize);
                var symbol = _symbols[row + 1, column];
                symbol.color = Color.white;
                symbol.texture = _symbolTextures[_grid[row, column]];
                Place(symbol.rectTransform, SymbolInset - ReelInteriorInset, SymbolInset,
                    SymbolSize, SymbolSize);
            }
        }
    }

    private void RefreshBalance()
    {
        bool affordable = _balanceValue >= MimicChestRules.SpinCost;
        _spinButton.interactable = affordable && !_spinning;
        _balanceLabel.text = affordable ? "ЖЕТОНЫ" : "НЕДОСТАТОЧНО ЖЕТОНОВ";
        _leverReady.color = affordable || _spinning ? Color.white : new Color(0.65f, 0.65f, 0.65f);
        DrawAmount(_balance, _balanceValue.ToString(System.Globalization.CultureInfo.InvariantCulture), 63);
    }

    private IEnumerator FloatReward(int payout)
    {
        int firstRow = Rows, firstColumn = Columns, lastColumn = 0;
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                if (!_winningCells[row, column]) continue;
                firstRow = Mathf.Min(firstRow, row);
                firstColumn = Mathf.Min(firstColumn, column);
                lastColumn = Mathf.Max(lastColumn, column);
            }
        }
        float x = ReelX + (firstColumn + lastColumn + 1) * CellSize / 2f;
        float y = ReelY + firstRow * CellSize + 40;
        DrawAmount(_payout, "+" + payout.ToString(System.Globalization.CultureInfo.InvariantCulture), 96);
        _payout.gameObject.SetActive(true);
        for (float elapsed = 0; elapsed < 1.15f; elapsed += Time.unscaledDeltaTime)
        {
            Place(_payout, x, y - elapsed * 100, 0, 0);
            _payoutOpacity.alpha = 1 - Mathf.InverseLerp(0.65f, 1.15f, elapsed);
            yield return null;
        }
        _payout.gameObject.SetActive(false);
        _rewardAnimation = null;
    }

    private void DrawAmount(RectTransform parent, string value, float height)
    {
        foreach (Transform child in parent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        float width = 3 * (value.Length - 1);
        foreach (char glyph in value) width += GlyphWidth(glyph, height);
        float x = -width / 2;
        foreach (char glyph in value)
        {
            float glyphWidth = GlyphWidth(glyph, height);
            AddImage(parent, "Glyph", x, 0, glyphWidth, height, _glyphTextures[GlyphIndex(glyph)]);
            x += glyphWidth + 3;
        }
    }

    private float GlyphWidth(char glyph, float height)
    {
        var texture = _glyphTextures[GlyphIndex(glyph)];
        return height * texture.width / texture.height;
    }

    private static int GlyphIndex(char glyph) => glyph == '+' ? 10 : glyph - '0';
    private static Texture2D Load(string path) => Resources.Load<Texture2D>(ResourcePath + path);

    private static RectTransform AddRect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, x, y, width, height);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static RawImage AddImage(Transform parent, string name, float x, float y, float width, float height,
        Texture2D texture = null)
    {
        var image = AddRect(parent, name, x, y, width, height).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static Button AddButton(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var image = AddRect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        image.color = color;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = color.a > 0 ? Selectable.Transition.ColorTint : Selectable.Transition.None;
        return button;
    }

    private static Text AddText(Transform parent, string name, float x, float y, float width, float height,
        string value, int fontSize, Color color)
    {
        var label = AtlasUi.Text(parent, value, fontSize, color);
        label.name = name;
        label.alignment = TextAnchor.MiddleCenter;
        Place(label.rectTransform, x, y, width, height);
        return label;
    }
}
