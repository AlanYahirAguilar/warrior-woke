using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    private HealthSystem _health;
    private PlayerStamina _stamina;
    private Canvas _canvas;
    private Image _healthFill;
    private Image _staminaFill;
    private Text _healthLabel;
    private Text _staminaLabel;

    private void Awake()
    {
        _health = GetComponent<HealthSystem>();
        _stamina = GetComponent<PlayerStamina>();
        BuildHUD();
    }

    private void OnEnable()
    {
        if (_health != null)
            _health.OnHealthChanged += UpdateHealth;
        if (_stamina != null)
            _stamina.OnStaminaChanged += UpdateStamina;
    }

    private void Start()
    {
        if (_health != null)
            UpdateHealth(_health.CurrentHealth, _health.MaxHealth);
        if (_stamina != null)
            UpdateStamina(_stamina.CurrentStamina, _stamina.MaxStamina);
    }

    private void OnDisable()
    {
        if (_health != null)
            _health.OnHealthChanged -= UpdateHealth;
        if (_stamina != null)
            _stamina.OnStaminaChanged -= UpdateStamina;
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }

    private void BuildHUD()
    {
        GameObject canvasObject = new GameObject("Player HUD");
        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect("Status Panel", canvasObject.transform);
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(24f, -24f);
        panel.sizeDelta = new Vector2(300f, 110f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.07f, 0.075f, 0.88f);

        _healthLabel = CreateLabel(panel, "Health Label", 14f);
        _healthFill = CreateBar(panel, "Health", 40f, new Color(0.88f, 0.22f, 0.2f));
        _staminaLabel = CreateLabel(panel, "Stamina Label", 62f);
        _staminaFill = CreateBar(panel, "Stamina", 88f, new Color(0.28f, 0.78f, 0.48f));
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static Text CreateLabel(RectTransform parent, string objectName, float top)
    {
        RectTransform rect = CreateRect(objectName, parent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, -top);
        rect.sizeDelta = new Vector2(272f, 20f);

        Text label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 14;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        return label;
    }

    private static Image CreateBar(RectTransform parent, string objectName, float top, Color fillColor)
    {
        RectTransform background = CreateRect(objectName + " Background", parent);
        background.anchorMin = new Vector2(0f, 1f);
        background.anchorMax = new Vector2(0f, 1f);
        background.pivot = new Vector2(0f, 1f);
        background.anchoredPosition = new Vector2(14f, -top);
        background.sizeDelta = new Vector2(272f, 12f);
        background.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.2f, 0.2f, 1f);

        RectTransform fillRect = CreateRect(objectName + " Fill", background);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = fillColor;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        return fill;
    }

    private void UpdateHealth(int current, int maximum)
    {
        _healthFill.fillAmount = maximum > 0 ? (float)current / maximum : 0f;
        _healthLabel.text = $"VIDA  {current} / {maximum}";
    }

    private void UpdateStamina(float current, float maximum)
    {
        _staminaFill.fillAmount = maximum > 0f ? current / maximum : 0f;
        _staminaLabel.text = $"ESTAMINA  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
    }
}