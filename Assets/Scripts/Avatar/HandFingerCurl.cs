using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public enum FingerInput
{
    Trigger,
    Grip,
    Thumb
}

[Serializable]
public sealed class FingerChain
{
    public string label = "Finger";
    public FingerInput input = FingerInput.Trigger;
    public Transform[] bones = new Transform[0];
    public float maxAngleDegrees = 80f;
    public Vector3 curlAxis = Vector3.right;
    [Range(0.3f, 1.2f)] public float jointFalloff = 1f;
}

[DefaultExecutionOrder(300)]
public sealed class HandFingerCurl : MonoBehaviour
{
    [SerializeField] private XRNode handNode = XRNode.LeftHand;
    [SerializeField] private Transform handBone;
    [SerializeField] private FingerChain[] chains = new FingerChain[0];
    [SerializeField] private float closeSpeed = 18f;
    [SerializeField] private float openSpeed = 12f;
    [SerializeField] private bool readFromDevice = true;
    [SerializeField] private bool usePreviewValues;
    [SerializeField, Range(0f, 1f)] private float previewTrigger;
    [SerializeField, Range(0f, 1f)] private float previewGrip;
    [SerializeField, Range(0f, 1f)] private float previewThumb;

    private Quaternion[][] restRotations;
    private float[] currentCurl;
    private float triggerTarget;
    private float gripTarget;
    private float thumbTarget;

    public float TriggerValue => triggerTarget;
    public float GripValue => gripTarget;
    public float ThumbValue => thumbTarget;

    private void Awake()
    {
        CaptureRestPose();
    }

    private void Update()
    {
        if (usePreviewValues)
        {
            triggerTarget = previewTrigger;
            gripTarget = previewGrip;
            thumbTarget = previewThumb;
            return;
        }

        if (readFromDevice)
        {
            ReadDevice();
        }
    }

    private void LateUpdate()
    {
        if (restRotations == null || restRotations.Length != chains.Length)
        {
            CaptureRestPose();
        }

        for (int i = 0; i < chains.Length; i++)
        {
            FingerChain chain = chains[i];
            float target = Mathf.Clamp01(TargetFor(chain.input));
            float speed = target > currentCurl[i] ? closeSpeed : openSpeed;
            currentCurl[i] = Mathf.MoveTowards(currentCurl[i], target, speed * Time.deltaTime);
            ApplyChain(i, chain);
        }
    }

    public void Configure(XRNode node, Transform hand, FingerChain[] fingerChains)
    {
        handNode = node;
        handBone = hand;
        chains = fingerChains;
        restRotations = null;
    }

    public void SetRemoteValues(float trigger, float grip, float thumb)
    {
        readFromDevice = false;
        triggerTarget = Mathf.Clamp01(trigger);
        gripTarget = Mathf.Clamp01(grip);
        thumbTarget = Mathf.Clamp01(thumb);
    }

    public void SetDeviceReading(bool enabled)
    {
        readFromDevice = enabled;
    }

    private void ReadDevice()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
        if (!device.isValid)
        {
            triggerTarget = 0f;
            gripTarget = 0f;
            thumbTarget = 0f;
            return;
        }

        triggerTarget = device.TryGetFeatureValue(CommonUsages.trigger, out float trigger) ? trigger : 0f;
        gripTarget = device.TryGetFeatureValue(CommonUsages.grip, out float grip) ? grip : 0f;

        bool thumbActive = ReadBool(device, CommonUsages.primaryButton)
            || ReadBool(device, CommonUsages.secondaryButton)
            || ReadBool(device, CommonUsages.primaryTouch)
            || ReadBool(device, CommonUsages.secondaryTouch)
            || ReadBool(device, CommonUsages.primary2DAxisTouch);

        thumbTarget = thumbActive ? 1f : 0f;
    }

    private static bool ReadBool(InputDevice device, InputFeatureUsage<bool> usage)
    {
        return device.TryGetFeatureValue(usage, out bool value) && value;
    }

    private float TargetFor(FingerInput input)
    {
        switch (input)
        {
            case FingerInput.Trigger:
                return triggerTarget;
            case FingerInput.Grip:
                return gripTarget;
            default:
                return thumbTarget;
        }
    }

    private void ApplyChain(int chainIndex, FingerChain chain)
    {
        Quaternion[] rest = restRotations[chainIndex];
        Vector3 axis = chain.curlAxis.sqrMagnitude > 0.0001f ? chain.curlAxis.normalized : Vector3.right;

        for (int j = 0; j < chain.bones.Length && j < rest.Length; j++)
        {
            Transform bone = chain.bones[j];
            if (bone == null)
            {
                continue;
            }

            float angle = chain.maxAngleDegrees * currentCurl[chainIndex] * Mathf.Pow(chain.jointFalloff, j);
            bone.localRotation = rest[j] * Quaternion.AngleAxis(angle, axis);
        }
    }

    private void CaptureRestPose()
    {
        restRotations = new Quaternion[chains.Length][];
        currentCurl = new float[chains.Length];

        for (int i = 0; i < chains.Length; i++)
        {
            Transform[] bones = chains[i].bones;
            restRotations[i] = new Quaternion[bones.Length];
            for (int j = 0; j < bones.Length; j++)
            {
                restRotations[i][j] = bones[j] != null ? bones[j].localRotation : Quaternion.identity;
            }
        }
    }

    [ContextMenu("Auto Assign Chains From Hand Bone")]
    private void AutoAssignChains()
    {
        if (handBone == null)
        {
            Debug.LogWarning("Assign the hand bone first.", this);
            return;
        }

        FingerInput[] defaultInputs = { FingerInput.Trigger, FingerInput.Grip, FingerInput.Thumb, FingerInput.Grip };
        List<FingerChain> found = new List<FingerChain>();

        for (int c = 0; c < handBone.childCount; c++)
        {
            List<Transform> bones = new List<Transform>();
            Transform current = handBone.GetChild(c);
            while (current != null)
            {
                bones.Add(current);
                current = current.childCount > 0 ? current.GetChild(0) : null;
            }

            FingerChain chain = new FingerChain();
            chain.label = "Finger " + c + " (" + handBone.GetChild(c).name + ")";
            chain.input = defaultInputs[Mathf.Min(c, defaultInputs.Length - 1)];
            chain.bones = bones.ToArray();
            found.Add(chain);
        }

        chains = found.ToArray();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Reset To Rest Pose")]
    private void ResetToRestPose()
    {
        if (restRotations == null)
        {
            return;
        }

        for (int i = 0; i < chains.Length && i < restRotations.Length; i++)
        {
            for (int j = 0; j < chains[i].bones.Length && j < restRotations[i].Length; j++)
            {
                if (chains[i].bones[j] != null)
                {
                    chains[i].bones[j].localRotation = restRotations[i][j];
                }
            }
        }
    }
}
