using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Aprender
{
    /// <summary>One input vocabulary for the world and the controller-driven interface.</summary>
    public static class GameControls
    {
        static bool uiCaptured;
        static int blockedWorldFrame = -1;
        static Gamepad describedPad;
        static bool describedNintendo;

        public static Gamepad Controller
        {
            get
            {
                var current = Gamepad.current;
                if (current != null && current.added && current.enabled) return current;
                foreach (var pad in Gamepad.all)
                    if (pad.added && pad.enabled) return pad;
                return null;
            }
        }

        public static bool Connected => Controller != null;
        public static bool UICaptured => uiCaptured || blockedWorldFrame == Time.frameCount;
        public static bool IsNintendo
        {
            get
            {
                var pad = Controller;
                if (pad == null) return false;
                if (pad == describedPad) return describedNintendo;
                describedPad = pad;
                var description = pad.layout + " " + pad.description.manufacturer + " " + pad.description.product;
                describedNintendo = description.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0
                    || description.IndexOf("Nintendo", StringComparison.OrdinalIgnoreCase) >= 0
                    || description.IndexOf("Pro Controller", StringComparison.OrdinalIgnoreCase) >= 0;
                return describedNintendo;
            }
        }

        // Read the actual layout's labels. Switch HID maps A to east; some native
        // macOS layouts already normalize A to south. Neither needs a hardcoded swap.
        public static ButtonControl ConfirmControl => FaceButton("A", IsNintendo ? Controller?.buttonEast : Controller?.buttonSouth);
        public static ButtonControl CancelControl => FaceButton("B", IsNintendo ? Controller?.buttonSouth : Controller?.buttonEast);
        public static string ConfirmLabel => Label(ConfirmControl, "A");
        public static string CancelLabel => Label(CancelControl, "B");
        public static string DeleteLabel => Label(Controller?.buttonWest, IsNintendo ? "Y" : "X");
        public static string WaveLabel => Label(Controller?.buttonNorth, IsNintendo ? "X" : "Y");
        public static string MenuLabel => IsNintendo ? "+" : "Menu";
        public static string HUDLabel => IsNintendo ? "−" : "View";

        public static bool ConfirmPressed => ConfirmControl?.wasPressedThisFrame == true;
        public static bool CancelPressed => CancelControl?.wasPressedThisFrame == true;
        public static bool MenuPressed => Controller?.startButton.wasPressedThisFrame == true;
        public static bool HUDPressed => Controller?.selectButton.wasPressedThisFrame == true;
        public static bool DeletePressed => Controller?.buttonWest.wasPressedThisFrame == true;

        public static Vector2 Navigation
        {
            get
            {
                var pad = Controller;
                if (pad == null) return Vector2.zero;
                var dpad = pad.dpad.ReadValue();
                return dpad.sqrMagnitude > .1f ? dpad : pad.leftStick.ReadValue();
            }
        }

        public static bool ControllerActivity => Navigation.sqrMagnitude > .16f
            || ConfirmPressed || CancelPressed || MenuPressed || HUDPressed || DeletePressed
            || Controller?.buttonNorth.wasPressedThisFrame == true
            || Controller?.leftShoulder.wasPressedThisFrame == true
            || Controller?.rightShoulder.wasPressedThisFrame == true;

        public static Vector2 Movement
        {
            get
            {
                if (UICaptured) return Vector2.zero;
                var value = Navigation;
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    value.x += (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                        - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                    value.y += (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                        - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                }
                return Vector2.ClampMagnitude(value, 1);
            }
        }

        public static bool InteractPressed => !UICaptured
            && (ConfirmPressed || Keyboard.current?.eKey.wasPressedThisFrame == true);
        public static bool WavePressed => !UICaptured
            && (Controller?.buttonNorth.wasPressedThisFrame == true || Keyboard.current?.spaceKey.wasPressedThisFrame == true);

        public static void SetUICapture(bool captured) => uiCaptured = captured;
        public static void BlockWorldForFrame() => blockedWorldFrame = Time.frameCount;

        static ButtonControl FaceButton(string text, ButtonControl fallback)
        {
            var pad = Controller;
            if (pad == null) return null;
            if (HasLabel(pad.buttonSouth, text)) return pad.buttonSouth;
            if (HasLabel(pad.buttonEast, text)) return pad.buttonEast;
            if (HasLabel(pad.buttonWest, text)) return pad.buttonWest;
            if (HasLabel(pad.buttonNorth, text)) return pad.buttonNorth;
            return fallback;
        }

        static bool HasLabel(ButtonControl control, string value) => string.Equals(control.shortDisplayName, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(control.displayName, value, StringComparison.OrdinalIgnoreCase);

        static string Label(ButtonControl control, string fallback)
        {
            if (control == null) return fallback;
            return string.IsNullOrEmpty(control.shortDisplayName) ? fallback : control.shortDisplayName;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            uiCaptured = false;
            blockedWorldFrame = -1;
            describedPad = null;
            describedNintendo = false;
        }
    }
}
