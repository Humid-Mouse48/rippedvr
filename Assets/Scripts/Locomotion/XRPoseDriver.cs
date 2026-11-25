using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(-100)]
public sealed class XRPoseDriver : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private bool requestFloorOrigin = true;

    private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
    private float nextOriginAttemptTime;
    private bool floorOriginSet;

    private void OnEnable()
    {
        Application.onBeforeRender += ApplyPoses;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ApplyPoses;
    }

    private void Update()
    {
        TrySetFloorOrigin();
        ApplyPoses();
    }

    private void ApplyPoses()
    {
        ApplyHeadPose();
        ApplyControllerPose(XRNode.LeftHand, leftHand);
        ApplyControllerPose(XRNode.RightHand, rightHand);
    }

    private void ApplyHeadPose()
    {
        if (head == null)
        {
            return;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!device.isValid)
        {
            device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        }

        if (!device.isValid || !IsTracked(device))
        {
            return;
        }

        if (device.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position)
            && device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation))
        {
            head.localPosition = position;
            head.localRotation = rotation;
            return;
        }

        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out position)
            && device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
        {
            head.localPosition = position;
            head.localRotation = rotation;
        }
    }

    private void ApplyControllerPose(XRNode node, Transform target)
    {
        if (target == null)
        {
            return;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !IsTracked(device))
        {
            return;
        }

        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
            && device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
        {
            target.localPosition = position;
            target.localRotation = rotation;
        }
    }

    private static bool IsTracked(InputDevice device)
    {
        return !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || tracked;
    }

    private void TrySetFloorOrigin()
    {
        if (!requestFloorOrigin || floorOriginSet || Time.unscaledTime < nextOriginAttemptTime)
        {
            return;
        }

        nextOriginAttemptTime = Time.unscaledTime + 1f;
        SubsystemManager.GetSubsystems(inputSubsystems);

        for (int i = 0; i < inputSubsystems.Count; i++)
        {
            XRInputSubsystem subsystem = inputSubsystems[i];
            if (subsystem.running && subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            {
                floorOriginSet = true;
            }
        }
    }
}
