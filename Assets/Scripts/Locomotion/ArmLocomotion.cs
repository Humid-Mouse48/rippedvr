
using System;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(200)]
[RequireComponent(typeof(Rigidbody))]
public sealed class ArmLocomotion : MonoBehaviour
{
    private const float MinimumSweepDistance = 0.000001f;
    private const float MinimumContactDisplacementSqr = 0.0000000001f;
    private const int SampleCapacity = 48;
    private const int OverlapBufferSize = 16;

    private struct VelocitySample
    {
        public float time;
        public Vector3 displacement;
        public float duration;
    }

    private sealed class HandState
    {
        public string name;
        public XRNode node;
        public Transform trackedTransform;
        public Transform visualTransform;
        public Vector3 localOffset;
        public SphereCollider collider;
        public AudioSource audioSource;
        public Vector3 trackedPosition;
        public Vector3 expectedTrackedPosition;
        public Vector3 solvedPosition;
        public Vector3 displacement;
        public Vector3 contactNormal;
        public Vector3 realMotion;
        public Vector3 slipperyImpulse;
        public int surfaceLayer;
        public bool valid;
        public bool needsRebase;
        public bool active;
        public bool inContact;
        public bool wasInContact;
        public bool gripping;
        public bool bounceRequested;
        public float bounceSpeed;
        public float nextBounceTime;
        public float nextFeedbackTime;
    }

    public event Action Respawned;

    [SerializeField] private LocomotionSettings settings;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;
    [SerializeField] private Transform leftHandVisual;
    [SerializeField] private Transform rightHandVisual;
    [SerializeField] private Transform respawnPoint;
    [SerializeField, Range(0.5f, 2f)] private float speedMultiplier = 1f;

    private readonly HandState leftHand = new HandState();
    private readonly HandState rightHand = new HandState();
    private readonly VelocitySample[] samples = new VelocitySample[SampleCapacity];
    private readonly Collider[] overlapBuffer = new Collider[OverlapBufferSize];

    private Rigidbody body;
    private SphereCollider headCollider;
    private SphereCollider bodyCollider;
    private SphereCollider probeCollider;
    private GameObject probeObject;
    private AudioClip contactClip;
    private Vector3 velocity;
    private Vector3 headFinalPosition;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private int sampleCount;
    private int sampleWriteIndex;
    private bool needsReset;
    private bool wasGripping;
    private bool headBlocked;
    private bool bodyBlocked;
    private float protectionTimer;

    public Vector3 Velocity => velocity;
    public bool LeftContact => leftHand.inContact;
    public bool RightContact => rightHand.inContact;
    public bool LeftGripping => leftHand.gripping;
    public bool RightGripping => rightHand.gripping;
    public bool HeadBlocked => headBlocked;
    public bool BodyBlocked => bodyBlocked;
    public string LeftSurface => DescribeSurface(leftHand);
    public string RightSurface => DescribeSurface(rightHand);

    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = Mathf.Clamp(value, 0.5f, 2f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        if (settings == null || headTransform == null || leftHandTransform == null || rightHandTransform == null)
        {
            Debug.LogError("ArmLocomotion is missing its settings or tracked transforms.", this);
            enabled = false;
            return;
        }

        if (settings.solidMask.value == 0)
        {
            Debug.LogWarning("LocomotionSettings.solidMask is empty. Run RIPPED > 1 - Setup Layers And Settings.", this);
        }

        if (respawnPoint != null)
        {
            spawnPosition = respawnPoint.position;
            spawnRotation = Quaternion.Euler(0f, respawnPoint.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
        else
        {
            spawnPosition = transform.position;
            spawnRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        contactClip = CreateContactClip();
        headCollider = CreateTriggerSphere("HeadCollider", settings.headRadius);
        bodyCollider = CreateTriggerSphere("BodyCollider", settings.bodyRadius);
        InitializeHand(leftHand, "LeftHand", XRNode.LeftHand, leftHandTransform, leftHandVisual, settings.leftHandLocalOffset);
        InitializeHand(rightHand, "RightHand", XRNode.RightHand, rightHandTransform, rightHandVisual, settings.rightHandLocalOffset);
        CreateProbe();
    }

    private void OnEnable()
    {
        needsReset = true;
    }

    private void OnDestroy()
    {
        if (probeObject != null)
        {
            Destroy(probeObject);
        }

        if (contactClip != null)
        {
            Destroy(contactClip);
        }
    }

    private void OnApplicationPause(bool paused)
    {
        needsReset = true;
        velocity = Vector3.zero;
    }

    private void OnApplicationFocus(bool focused)
    {
        needsReset = true;
        velocity = Vector3.zero;
    }

    public void Respawn()
    {
        TeleportTo(spawnPosition, spawnRotation);
    }

    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        velocity = Vector3.zero;
        needsReset = true;
        protectionTimer = settings.respawnProtectionSeconds;
        Respawned?.Invoke();
    }

    private void LateUpdate()
    {
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        if (needsReset || Time.unscaledDeltaTime > settings.hitchResetSeconds)
        {
            ResetTracking();
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, settings.maxFrameDeltaTime);
        protectionTimer = Mathf.Max(0f, protectionTimer - Time.deltaTime);

        if (!IsDeviceTracked(XRNode.Head))
        {
            needsReset = true;
            velocity = Vector3.zero;
            return;
        }

        Vector3 headPosition = headTransform.position;
        float headStepLimit = settings.maxHeadStepPerFrame;
        if ((headPosition - headFinalPosition).sqrMagnitude > headStepLimit * headStepLimit)
        {
            ResetTracking();
            return;
        }

        PrepareHand(leftHand);
        PrepareHand(rightHand);

        velocity += Vector3.down * (settings.gravity * dt);
        velocity = Vector3.ClampMagnitude(velocity, settings.maxSpeed);
        Vector3 passiveMotion = velocity * dt;

        SolveHand(leftHand, passiveMotion, dt);
        SolveHand(rightHand, passiveMotion, dt);

        Vector3 handDisplacement = CombineHandDisplacements() * speedMultiplier;
        Vector3 totalDisplacement = Vector3.ClampMagnitude(passiveMotion + handDisplacement, settings.maxDisplacementPerSecond * dt);

        Vector3 bodyOffset = Vector3.down * settings.bodyOffsetBelowHead;
        Vector3 desiredHead = headPosition + totalDisplacement;
        Vector3 allowedHead = MoveSphere(headFinalPosition, desiredHead, settings.headRadius, out Vector3 headNormal, out headBlocked);
        Vector3 desiredBody = allowedHead + bodyOffset;
        Vector3 allowedBody = MoveSphere(headFinalPosition + bodyOffset, desiredBody, settings.bodyRadius, out Vector3 bodyNormal, out bodyBlocked);
        allowedBody = Depenetrate(allowedBody, settings.bodyRadius);
        allowedHead = Depenetrate(allowedBody - bodyOffset, settings.headRadius);
        totalDisplacement = allowedHead - headPosition;

        transform.position += totalDisplacement;
        headFinalPosition = allowedHead;

        bool gripNow = leftHand.gripping || rightHand.gripping;
        UpdateVelocity(gripNow, totalDisplacement, headNormal, bodyNormal, dt);

        FinalizeHand(leftHand, totalDisplacement);
        FinalizeHand(rightHand, totalDisplacement);
        headCollider.transform.position = headFinalPosition;
        bodyCollider.transform.position = headFinalPosition + bodyOffset;
        wasGripping = gripNow;

        CheckHazards();
    }

    private void PrepareHand(HandState hand)
    {
        hand.active = false;
        hand.valid = IsDeviceTracked(hand.node);
        if (!hand.valid)
        {
            hand.needsRebase = true;
            return;
        }

        hand.trackedPosition = hand.trackedTransform.TransformPoint(hand.localOffset);
        Vector3 motion = hand.trackedPosition - hand.expectedTrackedPosition;
        float stepLimit = settings.maxHandStepPerFrame;
        if (hand.needsRebase || motion.sqrMagnitude > stepLimit * stepLimit)
        {
            hand.needsRebase = false;
            hand.solvedPosition = hand.trackedPosition;
            hand.expectedTrackedPosition = hand.trackedPosition;
            return;
        }

        hand.realMotion = motion;
        hand.active = true;
    }

    private void SolveHand(HandState hand, Vector3 passiveMotion, float dt)
    {
        hand.wasInContact = hand.inContact;
        hand.inContact = false;
        hand.gripping = false;
        hand.displacement = Vector3.zero;
        hand.slipperyImpulse = Vector3.zero;
        hand.bounceRequested = false;

        if (!hand.active)
        {
            return;
        }

        Vector3 target = hand.trackedPosition + passiveMotion;
        Vector3 from = hand.solvedPosition;
        Vector3 delta = target - from;
        float distance = delta.magnitude;
        if (distance < MinimumSweepDistance)
        {
            return;
        }

        Vector3 direction = delta / distance;
        if (!Physics.SphereCast(from, settings.handRadius, direction, out RaycastHit hit, distance + settings.skinWidth, settings.solidMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        float travel = Mathf.Clamp(hit.distance - settings.skinWidth, 0f, distance);
        Vector3 planted = from + direction * travel;
        Vector3 remaining = direction * (distance - travel);
        planted += Vector3.ProjectOnPlane(remaining, hit.normal) * settings.slideFactor;

        Vector3 pull = planted - target;
        Vector3 normalPart = Vector3.Project(pull, hit.normal);
        Vector3 tangentPart = pull - normalPart;
        int surfaceLayerIndex = hit.collider.gameObject.layer;
        int layerBit = 1 << surfaceLayerIndex;

        hand.inContact = true;
        hand.contactNormal = hit.normal;
        hand.surfaceLayer = surfaceLayerIndex;

        if ((settings.bouncyMask.value & layerBit) != 0)
        {
            hand.displacement = normalPart;
            float intoSurface = -Vector3.Dot(delta / dt, hit.normal);
            if (intoSurface > settings.bounceTriggerSpeed && Time.time >= hand.nextBounceTime)
            {
                hand.bounceRequested = true;
                hand.bounceSpeed = Mathf.Clamp(Mathf.Max(settings.bounceMinSpeed, intoSurface * settings.bounceRestitution), 0f, settings.maxSpeed);
                hand.nextBounceTime = Time.time + settings.bounceCooldownSeconds;
            }

            return;
        }

        if ((settings.noGripMask.value & layerBit) != 0)
        {
            hand.displacement = normalPart;
            return;
        }

        if ((settings.slipperyMask.value & layerBit) != 0)
        {
            hand.displacement = normalPart;
            hand.slipperyImpulse = tangentPart * settings.slipperyPushGain;
            return;
        }

        hand.displacement = pull;
        hand.gripping = true;
    }

    private Vector3 CombineHandDisplacements()
    {
        bool leftCounts = leftHand.inContact && leftHand.displacement.sqrMagnitude > MinimumContactDisplacementSqr;
        bool rightCounts = rightHand.inContact && rightHand.displacement.sqrMagnitude > MinimumContactDisplacementSqr;

        if (leftCounts && rightCounts)
        {
            if (settings.dualHandMode == DualHandMode.Largest)
            {
                return leftHand.displacement.sqrMagnitude >= rightHand.displacement.sqrMagnitude ? leftHand.displacement : rightHand.displacement;
            }

            return (leftHand.displacement + rightHand.displacement) * 0.5f;
        }

        if (leftCounts)
        {
            return leftHand.displacement;
        }

        if (rightCounts)
        {
            return rightHand.displacement;
        }

        return Vector3.zero;
    }

    private void UpdateVelocity(bool gripNow, Vector3 appliedDisplacement, Vector3 headNormal, Vector3 bodyNormal, float dt)
    {
        if (gripNow)
        {
            if (!wasGripping)
            {
                ClearSamples();
            }

            AddSample(appliedDisplacement, dt);
            velocity = Vector3.zero;
            return;
        }

        if (wasGripping)
        {
            Vector3 releaseVelocity = ComputeReleaseVelocity() * settings.jumpMultiplier;
            velocity = Vector3.ClampMagnitude(releaseVelocity, settings.maxJumpSpeed);
        }

        if (headBlocked)
        {
            velocity = RemoveIntoComponent(velocity, headNormal);
        }

        if (bodyBlocked)
        {
            velocity = RemoveIntoComponent(velocity, bodyNormal);
        }

        ApplyContactToVelocity(leftHand);
        ApplyContactToVelocity(rightHand);
        velocity = Vector3.ClampMagnitude(velocity, settings.maxSpeed);
    }

    private void ApplyContactToVelocity(HandState hand)
    {
        if (!hand.inContact)
        {
            return;
        }

        velocity = RemoveIntoComponent(velocity, hand.contactNormal);
        velocity += hand.slipperyImpulse;

        if (hand.bounceRequested)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, hand.contactNormal);
            velocity = tangent + hand.contactNormal * hand.bounceSpeed;
            hand.bounceRequested = false;
        }
    }

    private static Vector3 RemoveIntoComponent(Vector3 value, Vector3 normal)
    {
        float into = Vector3.Dot(value, normal);
        if (into < 0f)
        {
            value -= normal * into;
        }

        return value;
    }

    private void FinalizeHand(HandState hand, Vector3 appliedDisplacement)
    {
        if (!hand.valid)
        {
            UpdateHandVisual(hand);
            return;
        }

        Vector3 finalTracked = hand.trackedPosition + appliedDisplacement;
        Vector3 solved = finalTracked;

        if (hand.active)
        {
            solved = MoveSphere(hand.solvedPosition, finalTracked, settings.handRadius, out _, out _);
            solved = Depenetrate(solved, settings.handRadius);
            if ((solved - finalTracked).sqrMagnitude > settings.maxArmLength * settings.maxArmLength)
            {
                solved = finalTracked;
            }
        }

        hand.solvedPosition = solved;
        hand.expectedTrackedPosition = finalTracked;
        UpdateHandVisual(hand);
        EmitContactFeedback(hand);
    }

    private void EmitContactFeedback(HandState hand)
    {
        if (!hand.inContact || hand.wasInContact || Time.time < hand.nextFeedbackTime)
        {
            return;
        }

        float speed = hand.realMotion.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        float strength = Mathf.Clamp01(speed / settings.contactSpeedForMaxFeedback);
        SendHaptic(hand.node, Mathf.Lerp(settings.hapticMinAmplitude, settings.hapticMaxAmplitude, strength), settings.hapticDurationSeconds);
        hand.audioSource.PlayOneShot(contactClip, settings.contactSoundVolume * Mathf.Lerp(0.3f, 1f, strength));
        hand.nextFeedbackTime = Time.time + settings.contactFeedbackCooldownSeconds;
    }

    private Vector3 MoveSphere(Vector3 from, Vector3 to, float radius, out Vector3 firstNormal, out bool blocked)
    {
        firstNormal = Vector3.zero;
        blocked = false;
        Vector3 position = from;
        Vector3 remaining = to - from;

        for (int iteration = 0; iteration < 3; iteration++)
        {
            float distance = remaining.magnitude;
            if (distance < MinimumSweepDistance)
            {
                break;
            }

            Vector3 direction = remaining / distance;
            if (!Physics.SphereCast(position, radius, direction, out RaycastHit hit, distance + settings.skinWidth, settings.solidMask, QueryTriggerInteraction.Ignore))
            {
                position += remaining;
                break;
            }

            if (!blocked)
            {
                blocked = true;
                firstNormal = hit.normal;
            }

            float travel = Mathf.Clamp(hit.distance - settings.skinWidth, 0f, distance);
            position += direction * travel;
            remaining = Vector3.ProjectOnPlane(direction * (distance - travel), hit.normal);
        }

        return position;
    }

    private Vector3 Depenetrate(Vector3 position, float radius)
    {
        probeCollider.radius = radius;
        Vector3 corrected = position;

        for (int iteration = 0; iteration < settings.depenetrationIterations; iteration++)
        {
            int count = Physics.OverlapSphereNonAlloc(corrected, radius, overlapBuffer, settings.solidMask, QueryTriggerInteraction.Ignore);
            if (count == 0)
            {
                break;
            }

            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other is MeshCollider meshCollider && !meshCollider.convex)
                {
                    continue;
                }

                if (Physics.ComputePenetration(probeCollider, corrected, Quaternion.identity, other, other.transform.position, other.transform.rotation, out Vector3 direction, out float distance))
                {
                    corrected += direction * (distance + 0.0005f);
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return corrected;
    }

    private void CheckHazards()
    {
        if (protectionTimer > 0f)
        {
            return;
        }

        bool fell = headFinalPosition.y < settings.killPlaneHeight;
        bool touchedHazard = Physics.CheckSphere(headFinalPosition, settings.headRadius, settings.hazardMask, QueryTriggerInteraction.Collide);
        if (fell || touchedHazard)
        {
            Respawn();
        }
    }

    private void ResetTracking()
    {
        needsReset = false;
        velocity = Vector3.zero;
        wasGripping = false;
        headBlocked = false;
        bodyBlocked = false;
        ClearSamples();
        SnapBodyToGround();
        headFinalPosition = headTransform.position;
        ResetHand(leftHand);
        ResetHand(rightHand);
        headCollider.transform.position = headFinalPosition;
        bodyCollider.transform.position = headFinalPosition + Vector3.down * settings.bodyOffsetBelowHead;
    }

    private void ResetHand(HandState hand)
    {
        hand.valid = IsDeviceTracked(hand.node);
        hand.active = false;
        hand.inContact = false;
        hand.wasInContact = false;
        hand.gripping = false;
        hand.needsRebase = !hand.valid;
        if (!hand.valid)
        {
            return;
        }

        hand.trackedPosition = hand.trackedTransform.TransformPoint(hand.localOffset);
        hand.solvedPosition = hand.trackedPosition;
        hand.expectedTrackedPosition = hand.trackedPosition;
        UpdateHandVisual(hand);
    }

    private void SnapBodyToGround()
    {
        Vector3 bodyCenter = headTransform.position + Vector3.down * settings.bodyOffsetBelowHead;
        if (Physics.SphereCast(bodyCenter, settings.bodyRadius, Vector3.down, out RaycastHit hit, settings.resetSnapDistance, settings.solidMask, QueryTriggerInteraction.Ignore))
        {
            float drop = Mathf.Max(0f, hit.distance - settings.skinWidth);
            transform.position += Vector3.down * drop;
        }
    }

    private void ClearSamples()
    {
        sampleCount = 0;
        sampleWriteIndex = 0;
    }

    private void AddSample(Vector3 displacement, float duration)
    {
        samples[sampleWriteIndex] = new VelocitySample
        {
            time = Time.time,
            displacement = displacement,
            duration = duration
        };
        sampleWriteIndex = (sampleWriteIndex + 1) % SampleCapacity;
        sampleCount = Mathf.Min(sampleCount + 1, SampleCapacity);
    }

    private Vector3 ComputeReleaseVelocity()
    {
        float oldestAllowed = Time.time - settings.velocityWindowSeconds;
        Vector3 totalDisplacement = Vector3.zero;
        float totalDuration = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            int index = (sampleWriteIndex - 1 - i + SampleCapacity) % SampleCapacity;
            VelocitySample sample = samples[index];
            if (sample.time < oldestAllowed)
            {
                break;
            }

            totalDisplacement += sample.displacement;
            totalDuration += sample.duration;
        }

        if (totalDuration <= 0f)
        {
            return Vector3.zero;
        }

        return totalDisplacement / totalDuration;
    }

    private void InitializeHand(HandState hand, string handName, XRNode node, Transform tracked, Transform visual, Vector3 localOffset)
    {
        hand.name = handName;
        hand.node = node;
        hand.trackedTransform = tracked;
        hand.visualTransform = visual;
        hand.localOffset = localOffset;
        hand.collider = CreateTriggerSphere(handName + "Collider", settings.handRadius);
        hand.audioSource = hand.collider.gameObject.AddComponent<AudioSource>();
        hand.audioSource.playOnAwake = false;
        hand.audioSource.spatialBlend = 1f;
        hand.needsRebase = true;
    }

    private SphereCollider CreateTriggerSphere(string objectName, float radius)
    {
        GameObject sphereObject = new GameObject(objectName);
        sphereObject.transform.SetParent(transform, false);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            sphereObject.layer = playerLayer;
        }

        SphereCollider sphere = sphereObject.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = radius;
        return sphere;
    }

    private void CreateProbe()
    {
        probeObject = new GameObject("DepenetrationProbe");
        probeObject.hideFlags = HideFlags.HideInHierarchy;
        probeObject.layer = 2;
        probeCollider = probeObject.AddComponent<SphereCollider>();
        probeCollider.isTrigger = true;
        probeCollider.radius = 0.1f;
    }

    private static void UpdateHandVisual(HandState hand)
    {
        if (hand.visualTransform != null)
        {
            hand.visualTransform.position = hand.solvedPosition;
        }

        hand.collider.transform.position = hand.solvedPosition;
    }

    private static bool IsDeviceTracked(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            return true;
        }

        return !device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) || isTracked;
    }

    private static void SendHaptic(XRNode node, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid)
        {
            device.SendHapticImpulse(0u, amplitude, duration);
        }
    }

    private static string DescribeSurface(HandState hand)
    {
        if (!hand.inContact)
        {
            return "air";
        }

        string layerName = LayerMask.LayerToName(hand.surfaceLayer);
        return string.IsNullOrEmpty(layerName) ? "layer " + hand.surfaceLayer : layerName;
    }

    private static AudioClip CreateContactClip()
    {
        const int sampleRate = 44100;
        int length = (int)(sampleRate * 0.09f);
        float[] data = new float[length];
        System.Random random = new System.Random(1234);

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 45f);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float tone = Mathf.Sin(2f * Mathf.PI * 170f * t);
            data[i] = (noise * 0.5f + tone * 0.8f) * envelope * 0.6f;
        }

        AudioClip clip = AudioClip.Create("HandContact", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
