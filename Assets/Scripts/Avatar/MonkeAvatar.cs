using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(350)]
public sealed class MonkeAvatar : MonoBehaviour
{
    private sealed class Arm
    {
        public XRNode node;
        public float side;
        public Transform upper;
        public Transform lower;
        public Transform hand;
        public Transform target;
        public Transform anchor;
        public Quaternion upperRest;
        public Quaternion lowerRest;
        public Quaternion handCorrection;
        public float palmOffset;
    }

    [SerializeField] private Transform modelRoot;
    [SerializeField] private Transform headTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform leftTrackedAnchor;
    [SerializeField] private Transform rightTrackedAnchor;
    [SerializeField] private float armLengthMeters = 0.8f;
    [SerializeField] private float bodyBackOffset = 0.08f;
    [SerializeField] private float yawFollowSpeed = 10f;
    [SerializeField] private bool hideHead = true;
    [SerializeField] private Vector3 restPalmDirection = Vector3.down;
    [SerializeField] private float leftHandRollDegrees;
    [SerializeField] private float rightHandRollDegrees;

    private readonly List<Transform> allBones = new List<Transform>();
    private Arm leftArm;
    private Arm rightArm;
    private Quaternion alignment = Quaternion.identity;
    private Quaternion yawRotation = Quaternion.identity;
    private Vector3 headOffsetInModel;
    private bool ready;

    public void Configure(Transform model, Transform head, Transform leftTarget, Transform rightTarget, Transform leftAnchor, Transform rightAnchor)
    {
        modelRoot = model;
        headTarget = head;
        leftHandTarget = leftTarget;
        rightHandTarget = rightTarget;
        leftTrackedAnchor = leftAnchor;
        rightTrackedAnchor = rightAnchor;
    }

    private void Start()
    {
        if (modelRoot == null || headTarget == null)
        {
            Debug.LogError("MonkeAvatar is missing its model or head reference.", this);
            enabled = false;
            return;
        }

        modelRoot.GetComponentsInChildren(true, allBones);

        Transform shoulderL = FindBone("ShoulderL");
        Transform shoulderR = FindBone("ShoulderR");
        Transform upperL = FindBone("UpperArmL");
        Transform upperR = FindBone("UpperArmR");
        Transform lowerL = FindBone("LowerArmL");
        Transform lowerR = FindBone("LowerArmR");
        Transform handL = FindBone("HandL");
        Transform handR = FindBone("HandR");
        Transform head = FindBone("Head_");

        if (shoulderL == null || shoulderR == null || upperL == null || upperR == null || lowerL == null || lowerR == null || handL == null || handR == null)
        {
            Debug.LogError("MonkeAvatar could not find the arm bones in the model (expected names starting with ShoulderL, UpperArmL, LowerArmL, HandL and the R versions).", this);
            enabled = false;
            return;
        }

        modelRoot.localPosition = Vector3.zero;
        modelRoot.localRotation = Quaternion.identity;
        modelRoot.localScale = Vector3.one;

        Vector3 right = Vector3.ProjectOnPlane(shoulderR.position - shoulderL.position, Vector3.up);
        Vector3 modelForward = Vector3.Cross(right.normalized, Vector3.up);
        alignment = modelForward.sqrMagnitude > 0.0001f ? Quaternion.FromToRotation(modelForward.normalized, Vector3.forward) : Quaternion.identity;

        float measured = Vector3.Distance(upperR.position, lowerR.position) + Vector3.Distance(lowerR.position, handR.position);
        if (measured > 0.0001f)
        {
            modelRoot.localScale = Vector3.one * (armLengthMeters / measured);
        }

        leftArm = BuildArm(XRNode.LeftHand, -1f, upperL, lowerL, handL, leftHandTarget, leftTrackedAnchor, leftHandRollDegrees);
        rightArm = BuildArm(XRNode.RightHand, 1f, upperR, lowerR, handR, rightHandTarget, rightTrackedAnchor, rightHandRollDegrees);

        Transform reference = head != null ? head : shoulderL;
        headOffsetInModel = reference.position - modelRoot.position;

        SkinnedMeshRenderer[] skins = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].updateWhenOffscreen = true;
        }

        if (hideHead && head != null)
        {
            head.localScale = Vector3.one * 0.0001f;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(headTarget.forward, Vector3.up);
        yawRotation = flatForward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatForward.normalized, Vector3.up) : Quaternion.identity;
        ready = true;
    }

    private Arm BuildArm(XRNode node, float side, Transform upper, Transform lower, Transform hand, Transform target, Transform anchor, float rollDegrees)
    {
        Arm arm = new Arm
        {
            node = node,
            side = side,
            upper = upper,
            lower = lower,
            hand = hand,
            target = target,
            anchor = anchor,
            upperRest = upper.localRotation,
            lowerRest = lower.localRotation
        };

        Vector3 palm = restPalmDirection.sqrMagnitude > 0.0001f ? restPalmDirection.normalized : Vector3.down;
        FingerChain[] chains = BuildFingerChains(hand, palm, out Vector3 fingerDirection, out float meanBaseDistance);
        arm.palmOffset = meanBaseDistance * 0.6f;

        Vector3 palmFlat = Vector3.ProjectOnPlane(palm, fingerDirection);
        if (palmFlat.sqrMagnitude < 0.0001f)
        {
            palmFlat = Vector3.down;
        }

        Quaternion restFrame = Quaternion.LookRotation(fingerDirection, -palmFlat.normalized);
        Quaternion boneToFrame = Quaternion.Inverse(hand.rotation) * restFrame;
        Quaternion controllerFrame = Quaternion.LookRotation(Vector3.forward, Vector3.right * side);
        arm.handCorrection = controllerFrame * Quaternion.AngleAxis(rollDegrees, Vector3.forward) * Quaternion.Inverse(boneToFrame);

        if (chains.Length > 0)
        {
            HandFingerCurl curl = hand.gameObject.AddComponent<HandFingerCurl>();
            curl.Configure(node, hand, chains);
        }

        return arm;
    }

    private static FingerChain[] BuildFingerChains(Transform hand, Vector3 palm, out Vector3 averageDirection, out float meanBaseDistance)
    {
        List<List<Transform>> chains = new List<List<Transform>>();
        for (int i = 0; i < hand.childCount; i++)
        {
            List<Transform> bones = new List<Transform>();
            Transform current = hand.GetChild(i);
            while (current != null && !current.name.Contains("_end"))
            {
                bones.Add(current);
                current = current.childCount > 0 ? current.GetChild(0) : null;
            }

            if (bones.Count > 0)
            {
                chains.Add(bones);
            }
        }

        averageDirection = hand.forward;
        meanBaseDistance = 0.1f;

        if (chains.Count == 0)
        {
            return new FingerChain[0];
        }

        Vector3[] directions = new Vector3[chains.Count];
        Vector3 sum = Vector3.zero;
        float baseDistance = 0f;

        for (int i = 0; i < chains.Count; i++)
        {
            Transform last = chains[i][chains[i].Count - 1];
            Transform tip = last.childCount > 0 ? last.GetChild(0) : last;
            Vector3 direction = tip.position - chains[i][0].position;
            directions[i] = direction.sqrMagnitude > 0.000001f ? direction.normalized : hand.forward;
            sum += directions[i];
            baseDistance += Vector3.Distance(hand.position, chains[i][0].position);
        }

        averageDirection = sum.sqrMagnitude > 0.0001f ? sum.normalized : hand.forward;
        meanBaseDistance = baseDistance / chains.Count;

        int thumb = -1;
        if (chains.Count >= 3)
        {
            float widest = 0f;
            for (int i = 0; i < chains.Count; i++)
            {
                float angle = Vector3.Angle(directions[i], averageDirection);
                if (angle > widest)
                {
                    widest = angle;
                    thumb = i;
                }
            }
        }

        List<int> order = new List<int>();
        for (int i = 0; i < chains.Count; i++)
        {
            if (i != thumb)
            {
                order.Add(i);
            }
        }

        if (thumb >= 0)
        {
            Vector3 thumbBase = chains[thumb][0].position;
            order.Sort((a, b) => Vector3.Distance(chains[a][0].position, thumbBase).CompareTo(Vector3.Distance(chains[b][0].position, thumbBase)));
        }

        FingerChain[] result = new FingerChain[chains.Count];
        for (int i = 0; i < chains.Count; i++)
        {
            bool isThumb = i == thumb;
            int rank = order.IndexOf(i);

            Vector3 worldAxis = Vector3.Cross(directions[i], palm);
            Vector3 localAxis = worldAxis.sqrMagnitude > 0.0001f
                ? Quaternion.Inverse(chains[i][0].rotation) * worldAxis.normalized
                : Vector3.right;

            FingerChain chain = new FingerChain();
            chain.label = chains[i][0].name;
            chain.input = isThumb ? FingerInput.Thumb : (rank == 0 ? FingerInput.Trigger : FingerInput.Grip);
            chain.bones = chains[i].ToArray();
            chain.curlAxis = localAxis;
            chain.maxAngleDegrees = isThumb ? 55f : 85f;
            chain.jointFalloff = 1f;
            result[i] = chain;
        }

        return result;
    }

    private Transform FindBone(string namePrefix)
    {
        for (int i = 0; i < allBones.Count; i++)
        {
            if (allBones[i].name.StartsWith(namePrefix))
            {
                return allBones[i];
            }
        }

        return null;
    }

    private void LateUpdate()
    {
        if (!ready)
        {
            return;
        }

        UpdateBodyPose();
        SolveArm(leftArm);
        SolveArm(rightArm);
    }

    private void UpdateBodyPose()
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(headTarget.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.04f)
        {
            flatForward = Vector3.ProjectOnPlane(headTarget.up, Vector3.up) * (headTarget.forward.y < 0f ? 1f : -1f);
        }

        if (flatForward.sqrMagnitude > 0.0001f)
        {
            Quaternion targetYaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            float blend = 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime);
            yawRotation = Quaternion.Slerp(yawRotation, targetYaw, blend);
        }

        Quaternion rotation = yawRotation * alignment;
        modelRoot.rotation = rotation;
        modelRoot.position = headTarget.position - rotation * headOffsetInModel - yawRotation * Vector3.forward * bodyBackOffset;
    }

    private void SolveArm(Arm arm)
    {
        if (arm == null || arm.target == null || arm.anchor == null)
        {
            return;
        }

        Quaternion trackedRotation = arm.anchor.rotation;
        Vector3 wristTarget = arm.target.position - trackedRotation * Vector3.forward * arm.palmOffset;

        arm.upper.localRotation = arm.upperRest;
        arm.lower.localRotation = arm.lowerRest;

        Vector3 shoulderPosition = arm.upper.position;
        Vector3 elbowPosition = arm.lower.position;
        Vector3 wristPosition = arm.hand.position;
        float upperLength = Vector3.Distance(shoulderPosition, elbowPosition);
        float lowerLength = Vector3.Distance(elbowPosition, wristPosition);

        Vector3 toTarget = wristTarget - shoulderPosition;
        float distance = toTarget.magnitude;
        if (distance < 0.0001f || upperLength < 0.0001f)
        {
            return;
        }

        float reach = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) * 1.001f + 0.001f, (upperLength + lowerLength) * 0.999f);
        Vector3 direction = toTarget / distance;

        float cosShoulder = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength) / (2f * upperLength * reach), -1f, 1f);
        float along = upperLength * cosShoulder;
        float across = upperLength * Mathf.Sqrt(1f - cosShoulder * cosShoulder);

        Vector3 bodyRight = yawRotation * Vector3.right;
        Vector3 bodyForward = yawRotation * Vector3.forward;
        Vector3 hint = Vector3.down + bodyRight * (arm.side * 0.4f) - bodyForward * 0.3f;
        Vector3 hintAcross = Vector3.ProjectOnPlane(hint, direction);
        if (hintAcross.sqrMagnitude < 0.0001f)
        {
            hintAcross = Vector3.ProjectOnPlane(bodyRight * arm.side, direction);
        }

        hintAcross.Normalize();
        Vector3 desiredElbow = shoulderPosition + direction * along + hintAcross * across;
        Vector3 desiredWrist = shoulderPosition + direction * reach;

        arm.upper.rotation = Quaternion.FromToRotation(elbowPosition - shoulderPosition, desiredElbow - shoulderPosition) * arm.upper.rotation;
        arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position, desiredWrist - arm.lower.position) * arm.lower.rotation;
        arm.hand.rotation = trackedRotation * arm.handCorrection;
    }
}
