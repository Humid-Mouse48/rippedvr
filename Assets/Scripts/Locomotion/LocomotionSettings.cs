using UnityEngine;

public enum DualHandMode
{
    Average,
    Largest
}

[CreateAssetMenu(fileName = "LocomotionSettings", menuName = "RIPPED/Locomotion Settings")]
public sealed class LocomotionSettings : ScriptableObject
{
    [Header("Collision Shapes")]
    public float headRadius = 0.15f;
    public float handRadius = 0.07f;
    public float bodyRadius = 0.2f;
    public float bodyOffsetBelowHead = 0.4f;
    public float skinWidth = 0.01f;

    [Header("Surface Layers")]
    public LayerMask solidMask;
    public LayerMask noGripMask;
    public LayerMask slipperyMask;
    public LayerMask bouncyMask;
    public LayerMask hazardMask;

    [Header("Push And Jump")]
    public DualHandMode dualHandMode = DualHandMode.Average;
    public float jumpMultiplier = 1.1f;
    public float maxJumpSpeed = 6.5f;
    public float maxSpeed = 14f;
    public float velocityWindowSeconds = 0.1f;
    public float gravity = 9.81f;
    public float slideFactor = 0.03f;

    [Header("Special Surfaces")]
    public float slipperyPushGain = 2f;
    public float bounceRestitution = 0.9f;
    public float bounceMinSpeed = 5f;
    public float bounceTriggerSpeed = 0.5f;
    public float bounceCooldownSeconds = 0.2f;

    [Header("Safety")]
    public float maxArmLength = 1.5f;
    public float maxHandStepPerFrame = 1f;
    public float maxHeadStepPerFrame = 2f;
    public float maxDisplacementPerSecond = 25f;
    public float maxFrameDeltaTime = 0.05f;
    public float hitchResetSeconds = 0.25f;
    public int depenetrationIterations = 3;
    public float respawnProtectionSeconds = 0.5f;
    public float killPlaneHeight = -30f;
    public float resetSnapDistance = 3f;

    [Header("Hand Pose Offsets")]
    public Vector3 leftHandLocalOffset = Vector3.zero;
    public Vector3 rightHandLocalOffset = Vector3.zero;

    [Header("Feedback")]
    public float hapticMinAmplitude = 0.15f;
    public float hapticMaxAmplitude = 1f;
    public float hapticDurationSeconds = 0.05f;
    public float contactSoundVolume = 0.7f;
    public float contactSpeedForMaxFeedback = 3f;
    public float contactFeedbackCooldownSeconds = 0.08f;

    [Header("Player Model")]
    public float playerModelScale = 0.0035f;
}
