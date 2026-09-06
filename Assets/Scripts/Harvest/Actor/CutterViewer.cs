using UnityEngine;

[DisallowMultipleComponent]
public sealed class CutterViewer : MonoBehaviour
{
    private const float BuffTintStrength = 0.25f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] GameObject cutterVisuals;
    [SerializeField, Min(0f)] private float scaleMultiplier = 1f;
    [SerializeField] private float rotationDegreesPerSecond = 360f;
    [SerializeField, HideInInspector] private float cutterRange;

    private Renderer cutterRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Color originalColor;

    private void Awake()
    {
        cutterRenderer = cutterVisuals.GetComponentInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
        originalColor = cutterRenderer.sharedMaterial.GetColor(BaseColorId);
    }

    public void SetBuffTint(bool isActive)
    {
        Color color = isActive
            ? Color.Lerp(originalColor, Color.red, BuffTintStrength)
            : originalColor;
        color.a = originalColor.a;

        cutterRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        cutterRenderer.SetPropertyBlock(propertyBlock);
    }

    public void SetRange(float range)
    {
        cutterRange = Mathf.Max(0f, range);
        ApplyScale();
    }

    private void Update()
    {
        cutterVisuals.transform.Rotate(
            Vector3.left,
            rotationDegreesPerSecond * Time.deltaTime,
            Space.Self);
    }

    private void OnValidate()
    {
        scaleMultiplier = Mathf.Max(0f, scaleMultiplier);
        ApplyScale();
    }

    private void ApplyScale()
    {
        cutterVisuals.transform.localScale =
            Vector3.one * (cutterRange * scaleMultiplier);
    }
}
