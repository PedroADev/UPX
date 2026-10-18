using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aprender
{
    /// <summary>A captured touch stays with this stick while another finger presses an action.</summary>
    public sealed class TouchStick : VisualElement, IDisposable
    {
        const float DeadZone = .13f;
        readonly VisualElement knob;
        readonly Action<Vector2> changed;
        int activePointer = -1;
        Vector2 deflection;
        bool inputEnabled = true;

        public Vector2 Value { get; private set; }
        public bool IsPressed => activePointer >= 0;
        public bool InputEnabled
        {
            get => inputEnabled;
            set
            {
                inputEnabled = value;
                if (!value) Cancel();
                SetEnabled(value);
            }
        }

        public TouchStick(Action<Vector2> onChanged)
        {
            changed = onChanged;
            name = "touch-movement";
            AddToClassList("touch-stick");
            focusable = false;
            tooltip = "Arraste para caminhar";
            knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("touch-stick__knob");
            Add(knob);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            RegisterCallback<DetachFromPanelEvent>(_ => Cancel());
            RegisterCallback<GeometryChangedEvent>(_ => PositionKnob());
            knob.RegisterCallback<GeometryChangedEvent>(_ => PositionKnob());
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (!inputEnabled || activePointer >= 0 || evt.button != 0) return;
            activePointer = evt.pointerId;
            this.CapturePointer(activePointer);
            AddToClassList("pressed");
            ReadPosition(evt.localPosition);
            evt.StopImmediatePropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!inputEnabled || evt.pointerId != activePointer) return;
            ReadPosition(evt.localPosition);
            evt.StopImmediatePropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != activePointer) return;
            Cancel();
            evt.StopImmediatePropagation();
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != activePointer) return;
            Cancel();
            evt.StopImmediatePropagation();
        }

        void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == activePointer) Cancel();
        }

        void ReadPosition(Vector2 localPosition)
        {
            var center = contentRect.center;
            var radius = Mathf.Max(1, Mathf.Min(contentRect.width, contentRect.height) * .32f);
            deflection = Vector2.ClampMagnitude((localPosition - center) / radius, 1);
            var magnitude = deflection.magnitude;
            Value = magnitude < DeadZone ? Vector2.zero : new Vector2(deflection.x, -deflection.y)
                * ((magnitude - DeadZone) / ((1 - DeadZone) * magnitude));
            PositionKnob();
            changed?.Invoke(Value);
        }

        void PositionKnob()
        {
            var radius = Mathf.Max(1, Mathf.Min(contentRect.width, contentRect.height) * .32f);
            var width = float.IsNaN(knob.resolvedStyle.width) ? 0 : knob.resolvedStyle.width;
            var height = float.IsNaN(knob.resolvedStyle.height) ? 0 : knob.resolvedStyle.height;
            knob.style.translate = new Translate(deflection.x * radius - width * .5f, deflection.y * radius - height * .5f);
        }

        public void Cancel()
        {
            // Clear before releasing: PointerCaptureOutEvent can be dispatched synchronously.
            var pointer = activePointer;
            activePointer = -1;
            deflection = Vector2.zero;
            Value = Vector2.zero;
            RemoveFromClassList("pressed");
            PositionKnob();
            changed?.Invoke(Vector2.zero);
            if (pointer >= 0 && this.HasPointerCapture(pointer)) this.ReleasePointer(pointer);
        }

        public void Dispose() => Cancel();
    }
}
