using UnityEngine;

public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float sprintDrainPerSecond = 20f;
    [SerializeField] private float recoveryPerSecond = 15f;
    [SerializeField] private float sprintResumeThreshold = 20f;

    public event System.Action<float, float> OnStaminaChanged;

    public float CurrentStamina { get; private set; }
    public float MaxStamina => maxStamina;
    public bool CanSprint => CurrentStamina >= sprintResumeThreshold;

    private PlayerMovement _movement;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        CurrentStamina = maxStamina;
    }

    private void Update()
    {
        float previousStamina = CurrentStamina;
        float rate = _movement != null && _movement.IsSprint
            ? -sprintDrainPerSecond
            : recoveryPerSecond;

        CurrentStamina = Mathf.Clamp(CurrentStamina + rate * Time.deltaTime, 0f, maxStamina);

        if (!Mathf.Approximately(previousStamina, CurrentStamina))
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
    }
}