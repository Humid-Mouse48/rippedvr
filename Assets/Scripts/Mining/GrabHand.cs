using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(400)]
public sealed class GrabHand : MonoBehaviour
{
    private const int SampleCount = 10;

    [SerializeField] private XRNode handNode = XRNode.LeftHand;
    [SerializeField] private Transform handTransform;
    [SerializeField] private Transform rotationSource;
    [SerializeField] private float grabRadius = 0.12f;
    [SerializeField, Range(0.1f, 0.9f)] private float pressThreshold = 0.6f;
    [SerializeField, Range(0.05f, 0.8f)] private float releaseThreshold = 0.35f;
    [SerializeField] private float velocityWindowSeconds = 0.1f;
    [SerializeField] private bool usePreviewGrip;
    [SerializeField, Range(0f, 1f)] private float previewGrip;

    private readonly Collider[] overlaps = new Collider[32];
    private readonly Vector3[] positions = new Vector3[SampleCount];
    private readonly Quaternion[] rotations = new Quaternion[SampleCount];
    private readonly float[] times = new float[SampleCount];
    private int sampleIndex;
    private int sampleTotal;
    private bool gripDown;
    private Grabbable held;

    public Transform HandTransform => handTransform;
    public XRNode Node => handNode;
    public Grabbable Held => held;

    private void Awake()
    {
        if (handTransform == null)
        {
            handTransform = transform;
        }
    }

    private void LateUpdate()
    {
        if (rotationSource != null)
        {
            handTransform.rotation = rotationSource.rotation;
        }

        RecordSample();

        float grip = ReadGrip();
        if (!gripDown && grip >= pressThreshold)
        {
            gripDown = true;
            TryGrab();
        }
        else if (gripDown && grip <= releaseThreshold)
        {
            gripDown = false;
            Release();
        }
    }

    public void Configure(XRNode node, Transform hand, Transform trackedAnchor)
    {
        handNode = node;
        handTransform = hand;
        rotationSource = trackedAnchor;
    }

    public void ForceRelease()
    {
        Release();
    }

    public void Pulse(float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
        if (device.isValid)
        {
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }
    }

    private float ReadGrip()
    {
        if (usePreviewGrip)
        {
            return previewGrip;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float grip))
        {
            return grip;
        }

        return 0f;
    }

    private void TryGrab()
    {
        if (held != null)
        {
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(handTransform.position, grabRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        Grabbable best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Grabbable candidate = overlaps[i].GetComponentInParent<Grabbable>();
            if (candidate == null || candidate.IsHeld && candidate.Holder == this)
            {
                continue;
            }

            float distance = candidate.SqrDistanceTo(handTransform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best == null)
        {
            return;
        }

        held = best;
        held.BeginGrab(this);
        Pulse(0.4f, 0.05f);
    }

    private void Release()
    {
        if (held == null)
        {
            return;
        }

        Grabbable released = held;
        held = null;
        GetVelocities(out Vector3 velocity, out Vector3 angular);
        released.EndGrab(velocity, angular);
    }

    private void RecordSample()
    {
        positions[sampleIndex] = handTransform.position;
        rotations[sampleIndex] = handTransform.rotation;
        times[sampleIndex] = Time.time;
        sampleIndex = (sampleIndex + 1) % SampleCount;
        sampleTotal = Mathf.Min(sampleTotal + 1, SampleCount);
    }

    private void GetVelocities(out Vector3 velocity, out Vector3 angular)
    {
        velocity = Vector3.zero;
        angular = Vector3.zero;

        if (sampleTotal < 2)
        {
            return;
        }

        int newest = (sampleIndex - 1 + SampleCount) % SampleCount;
        int oldest = newest;
        for (int i = 1; i < sampleTotal; i++)
        {
            int index = (newest - i + SampleCount) % SampleCount;
            if (times[newest] - times[index] > velocityWindowSeconds)
            {
                break;
            }

            oldest = index;
        }

        float dt = times[newest] - times[oldest];
        if (dt < 0.005f)
        {
            return;
        }

        velocity = (positions[newest] - positions[oldest]) / dt;

        Quaternion delta = rotations[newest] * Quaternion.Inverse(rotations[oldest]);
        delta.ToAngleAxis(out float degrees, out Vector3 axis);
        if (degrees > 180f)
        {
            degrees -= 360f;
        }

        angular = axis * (degrees * Mathf.Deg2Rad / dt);
        angular = Vector3.ClampMagnitude(angular, 20f);
    }
}
