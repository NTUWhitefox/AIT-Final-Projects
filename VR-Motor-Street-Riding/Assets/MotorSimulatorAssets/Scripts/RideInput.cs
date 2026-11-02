using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace AIT.Riding
{
    public struct RideInputFrame
    {
        public float steering, throttle, brake, reverse;
        // Hardware adapters set this when a NEW sensor sample arrives, using
        // Time.realtimeSinceStartup. Re-reading cached data must not refresh it.
        public float sampleTime;
        public bool connected, pausePressed, stopHeld, recenterPressed, stagePressed, comfortPressed;
    }

    // Future sensor adapters only need to supply normalised values through this
    // boundary. Steering is a requested direction, NOT measured steering torque.
    public abstract class RideInputProvider : MonoBehaviour
    {
        public abstract RideInputFrame ReadInput();
    }

    public sealed class RideInput : RideInputProvider
    {
        public RideTestSettings settings;
        public RideInputProvider externalProvider;
        public string DeviceDescription { get; private set; } = "Not sampled";
        bool wasPause, wasRecenter, wasStage, wasComfort;
        RideInputSource lastSource;

        static float Axis(InputDevice d, UnityEngine.XR.InputFeatureUsage<float> usage)
            => d.TryGetFeatureValue(usage, out float v) ? v : 0;
        static bool Button(InputDevice d, UnityEngine.XR.InputFeatureUsage<bool> usage)
            => d.TryGetFeatureValue(usage, out bool v) && v;

        public override RideInputFrame ReadInput()
        {
            var f = new RideInputFrame();
            bool pause = false, recenter = false, stage = false, comfort = false;
            if (lastSource != settings.inputSource)
            {
                wasPause = wasRecenter = wasStage = wasComfort = true;
                lastSource = settings.inputSource;
            }
            switch (settings.inputSource)
            {
                case RideInputSource.ViveControllers:
                    var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                    var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                    f.connected = left.isValid && right.isValid;
                    DeviceDescription = $"L:{left.name} / R:{right.name}";
                    if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)) f.steering = axis.x;
                    f.throttle = Axis(right, CommonUsages.trigger);
                    f.brake = Axis(left, CommonUsages.trigger);
                    f.reverse = Button(right, CommonUsages.secondaryButton) ? 1 : 0;
                    pause = Button(left, CommonUsages.primaryButton);
                    f.stopHeld = Button(left, CommonUsages.gripButton);
                    recenter = Button(right, CommonUsages.primaryButton);
                    stage = Button(left, CommonUsages.secondaryButton);
                    break;
                case RideInputSource.Gamepad:
                    var pad = Gamepad.current;
                    f.connected = pad != null;
                    DeviceDescription = pad == null ? "No gamepad" : pad.displayName;
                    if (pad != null)
                    {
                        f.steering = pad.leftStick.x.ReadValue();
                        f.throttle = pad.rightTrigger.ReadValue();
                        f.brake = pad.leftTrigger.ReadValue();
                        f.reverse = pad.buttonEast.isPressed ? 1 : 0;
                        pause = pad.startButton.isPressed;
                        f.stopHeld = pad.leftShoulder.isPressed;
                        recenter = pad.buttonSouth.isPressed;
                        stage = pad.buttonWest.isPressed;
                        comfort = pad.buttonNorth.isPressed;
                    }
                    break;
                case RideInputSource.Keyboard:
                    var k = Keyboard.current;
                    f.connected = k != null;
                    DeviceDescription = "Keyboard (desktop verification)";
                    if (k != null)
                    {
                        f.steering = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
                        f.throttle = k.wKey.isPressed ? 1 : 0;
                        f.brake = k.sKey.isPressed ? 1 : 0;
                        f.reverse = k.rKey.isPressed ? 1 : 0;
                        pause = k.pKey.isPressed;
                        f.stopHeld = k.spaceKey.isPressed;
                        recenter = k.cKey.isPressed;
                        stage = k.tabKey.isPressed;
                        comfort = k.bKey.isPressed;
                    }
                    break;
                case RideInputSource.External:
                    DeviceDescription = externalProvider ? externalProvider.GetType().Name : "No external provider";
                    if (externalProvider && externalProvider != this && externalProvider.isActiveAndEnabled)
                        return Validate(externalProvider.ReadInput());
                    return f;
            }
            f.steering = ApplyDeadzone(f.steering, settings.steeringDeadzone);
            f.throttle = Mathf.Clamp01(f.throttle);
            f.brake = Mathf.Clamp01(f.brake);
            f.pausePressed = pause && !wasPause;
            f.recenterPressed = recenter && !wasRecenter;
            f.stagePressed = stage && !wasStage;
            f.comfortPressed = comfort && !wasComfort;
            wasPause = pause; wasRecenter = recenter; wasStage = stage; wasComfort = comfort;
            f.sampleTime = Time.realtimeSinceStartup;
            return Validate(f);
        }

        public static RideInputFrame Validate(RideInputFrame frame)
        {
            if (float.IsNaN(frame.steering) || float.IsInfinity(frame.steering) ||
                float.IsNaN(frame.throttle) || float.IsInfinity(frame.throttle) ||
                float.IsNaN(frame.brake) || float.IsInfinity(frame.brake) ||
                float.IsNaN(frame.reverse) || float.IsInfinity(frame.reverse) ||
                float.IsNaN(frame.sampleTime) || float.IsInfinity(frame.sampleTime))
                return new RideInputFrame();
            frame.steering = Mathf.Clamp(frame.steering, -1, 1);
            frame.throttle = Mathf.Clamp01(frame.throttle);
            frame.brake = Mathf.Clamp01(frame.brake);
            frame.reverse = Mathf.Clamp01(frame.reverse);
            return frame;
        }

        public static float ApplyDeadzone(float value, float deadzone)
        {
            deadzone = Mathf.Clamp(deadzone, 0, 0.95f);
            return Mathf.Sign(value) * Mathf.Clamp01((Mathf.Abs(value) - deadzone) / (1 - deadzone));
        }
    }
}
