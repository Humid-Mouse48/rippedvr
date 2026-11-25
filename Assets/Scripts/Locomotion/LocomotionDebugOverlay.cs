using System.Text;
using UnityEngine;
using UnityEngine.XR;

public sealed class LocomotionDebugOverlay : MonoBehaviour
{
    [SerializeField] private ArmLocomotion locomotion;
    [SerializeField] private Vector3 localPosition = new Vector3(0f, -0.2f, 0.9f);
    [SerializeField] private float refreshSeconds = 0.1f;
    [SerializeField] private XRNode toggleNode = XRNode.RightHand;

    private readonly StringBuilder builder = new StringBuilder(256);
    private TextMesh textMesh;
    private float smoothedFrameSeconds = 0.0139f;
    private float nextRefreshTime;
    private bool visible = true;
    private bool toggleWasPressed;

    private void Awake()
    {
        if (locomotion == null)
        {
            locomotion = GetComponentInParent<ArmLocomotion>();
        }

        CreateText();
    }

    private void Update()
    {
        smoothedFrameSeconds = Mathf.Lerp(smoothedFrameSeconds, Time.unscaledDeltaTime, 0.05f);
        PollToggle();

        if (!visible || locomotion == null || Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshSeconds;
        Refresh();
    }

    private void PollToggle()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(toggleNode);
        bool pressed = device.isValid && device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool value) && value;

        if (pressed && !toggleWasPressed)
        {
            visible = !visible;
            textMesh.gameObject.SetActive(visible);
        }

        toggleWasPressed = pressed;
    }

    private void Refresh()
    {
        Vector3 velocity = locomotion.Velocity;
        float frameSeconds = Mathf.Max(smoothedFrameSeconds, 0.0001f);

        builder.Clear();
        builder.Append("speed ").Append(velocity.magnitude.ToString("0.00")).Append(" m/s   vy ").Append(velocity.y.ToString("0.00")).Append('\n');
        builder.Append("L ").Append(Describe(locomotion.LeftContact, locomotion.LeftGripping, locomotion.LeftSurface));
        builder.Append("   R ").Append(Describe(locomotion.RightContact, locomotion.RightGripping, locomotion.RightSurface)).Append('\n');
        builder.Append("head blocked ").Append(locomotion.HeadBlocked ? "yes" : "no");
        builder.Append("   body blocked ").Append(locomotion.BodyBlocked ? "yes" : "no").Append('\n');
        builder.Append("frame ").Append((frameSeconds * 1000f).ToString("0.0")).Append(" ms  ").Append((1f / frameSeconds).ToString("0")).Append(" fps").Append('\n');
        builder.Append("speed x").Append(locomotion.SpeedMultiplier.ToString("0.00"));

        textMesh.text = builder.ToString();
    }

    private void CreateText()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject textObject = new GameObject("DebugOverlayText");
        textObject.transform.SetParent(transform, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;

        textMesh = textObject.AddComponent<TextMesh>();
        textMesh.font = font;
        textMesh.fontSize = 64;
        textMesh.characterSize = 0.006f;
        textMesh.anchor = TextAnchor.UpperCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = new Color(0.4f, 1f, 0.4f);
        textMesh.text = "debug";

        textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    private static string Describe(bool contact, bool gripping, string surface)
    {
        if (!contact)
        {
            return "air";
        }

        return (gripping ? "GRIP " : "touch ") + surface;
    }
}
