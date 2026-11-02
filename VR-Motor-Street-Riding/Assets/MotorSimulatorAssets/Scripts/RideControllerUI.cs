using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace AIT.Riding
{
    // SampleScene uses XRUIInputModule + TrackedDeviceGraphicRaycaster.
    // These UI-only rays use the Starter Assets Position/Rotation fallback
    // and UI Press bindings, without adding locomotion to the seated rig.
    public sealed class RideControllerUI : MonoBehaviour
    {
        public IReadOnlyList<XRRayInteractor> Rays => rays;
        readonly List<XRRayInteractor> rays = new List<XRRayInteractor>();
        readonly List<InputAction> actions = new List<InputAction>();
        Material lineMaterial;

        public void Configure()
        {
            var module = GetComponent<XRUIInputModule>();
            module.enableXRInput = true;
            module.enableMouseInput = module.enableTouchInput = module.enableGamepadInput = module.enableJoystickInput = false;
            var manager = gameObject.AddComponent<XRInteractionManager>();
            lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lineMaterial.SetColor("_BaseColor", new Color(.2f, .9f, 1));
            CreateRay("LeftHand", manager);
            CreateRay("RightHand", manager);
        }
        void CreateRay(string hand, XRInteractionManager manager)
        {
            var obj = new GameObject(hand + " UI Ray");
            obj.SetActive(false); obj.layer = 2; obj.transform.SetParent(transform, false);
            string device = "<XRController>{" + hand + "}";
            var driver = obj.AddComponent<TrackedPoseDriver>();
            var position = Action(hand + " Position", InputActionType.Value, "Vector3");
            position.AddCompositeBinding("Vector3Fallback").With("first", device + "/pointerPosition").With("second", device + "/devicePosition");
            var rotation = Action(hand + " Rotation", InputActionType.Value, "Quaternion");
            rotation.AddCompositeBinding("QuaternionFallback").With("first", device + "/pointerRotation").With("second", device + "/deviceRotation");
            var tracking = Action(hand + " Tracking", InputActionType.Value, "Integer"); tracking.AddBinding(device + "/trackingState");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            driver.trackingStateInput = new InputActionProperty(tracking);
            var ray = obj.AddComponent<XRRayInteractor>();
            ray.interactionManager = manager; ray.enableUIInteraction = true;
            ray.maxRaycastDistance = 5; ray.raycastMask = 1 << 2; // Same layer as the menu.
            ray.referenceFrame = transform;
            var press = new XRInputButtonReader("UI Press", inputSourceMode: XRInputButtonReader.InputSourceMode.InputAction);
            press.inputActionPerformed.AddBinding(device + "/{TriggerButton}");
            press.inputActionValue.AddBinding(device + "/{Trigger}");
            actions.Add(press.inputActionPerformed); actions.Add(press.inputActionValue);
            ray.uiPressInput = press;
            var line = obj.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial;
            var visual = obj.AddComponent<XRInteractorLineVisual>(); visual.lineWidth = .003f;
            rays.Add(ray); obj.SetActive(true);
        }
        InputAction Action(string name, InputActionType type, string control)
        {
            var action = new InputAction(name, type, expectedControlType: control);
            actions.Add(action); return action;
        }
        public void SetMenuOpen(bool open)
        {
            foreach (var ray in rays) ray.gameObject.SetActive(open);
        }
        void OnDestroy()
        {
            // Disable consumers before releasing their embedded actions.
            foreach (var ray in rays) if (ray) ray.gameObject.SetActive(false);
            foreach (var action in actions) action.Dispose();
            if (lineMaterial)
            { if (Application.isPlaying) Destroy(lineMaterial); else DestroyImmediate(lineMaterial); }
        }
    }
}
