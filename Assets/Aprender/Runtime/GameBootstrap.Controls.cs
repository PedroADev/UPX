using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Aprender
{
    // Capture interface input before WorldController reads movement in its Update.
    [DefaultExecutionOrder(-100)]
    public sealed partial class GameBootstrap
    {
        VisualElement controllerFocus, controllerScope, keyboardOverlay;
        Label controllerHint, keyboardPreview, keyboardError;
        TextField keyboardTarget;
        string keyboardOriginal, keyboardValue;
        readonly List<Button> letterKeys = new List<Button>();
        bool controllerMode, hudNavigation, dispatchingControllerSubmit, keyboardUpper;
        Vector2Int previousNavigation;
        float nextNavigationTime;
        int controllerUpdateFrame = -1;
        int controllerEscapeFrame = -1;
        bool ControllerConsumedEscape => controllerEscapeFrame == Time.frameCount;

        void InitializeControllerUI()
        {
            root.RegisterCallback<NavigationMoveEvent>(SuppressControllerMove, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(SuppressControllerSubmit, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(SuppressControllerCancel, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(ControllerPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(ControllerKeyDown, TrickleDown.TrickleDown);
            GameControls.SetUICapture(true);
        }

        void DisposeControllerUI()
        {
            if (root != null)
            {
                root.UnregisterCallback<NavigationMoveEvent>(SuppressControllerMove, TrickleDown.TrickleDown);
                root.UnregisterCallback<NavigationSubmitEvent>(SuppressControllerSubmit, TrickleDown.TrickleDown);
                root.UnregisterCallback<NavigationCancelEvent>(SuppressControllerCancel, TrickleDown.TrickleDown);
                root.UnregisterCallback<PointerDownEvent>(ControllerPointerDown, TrickleDown.TrickleDown);
                root.UnregisterCallback<KeyDownEvent>(ControllerKeyDown, TrickleDown.TrickleDown);
            }
            GameControls.SetUICapture(false);
        }

        void ControllerUIReset()
        {
            if (keyboardOverlay != null) FinishControllerKeyboard(false);
            ClearControllerFocus();
            controllerScope = null;
            controllerHint = null;
            hudNavigation = false;
            previousNavigation = Vector2Int.zero;
            GameControls.SetUICapture(true);
            GameControls.BlockWorldForFrame();
        }

        void UpdateControllerUI()
        {
            if (root == null || controllerUpdateFrame == Time.frameCount) return;
            controllerUpdateFrame = Time.frameCount;
            if (GameControls.ControllerActivity) controllerMode = true;
            if (!GameControls.Connected)
            {
                controllerMode = false;
                hudNavigation = false;
                ClearControllerFocus();
            }

            var inWorld = session != null && session.IsJoined;
            var focusedText = root.focusController?.focusedElement as VisualElement;
            var editingText = FindControllerTarget(focusedText) is TextField;
            var captured = !inWorld || modalOpen || busy || hudNavigation || keyboardOverlay != null || editingText;
            GameControls.SetUICapture(captured);
            root.EnableInClassList("controller-mode", GameControls.Connected);
            root.EnableInClassList("controller-navigating", GameControls.Connected && captured);
            if (captured && world != null) world.TouchMove = Vector2.zero;
            RefreshControllerHint(inWorld);

            if (busy) return;
            if (keyboardOverlay != null && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                controllerEscapeFrame = Time.frameCount;
                FinishControllerKeyboard(false);
                return;
            }
            if (GameControls.MenuPressed)
            {
                GameControls.BlockWorldForFrame();
                if (keyboardOverlay != null) FinishControllerKeyboard(true);
                else if (modalOpen) CloseModal();
                else if (inWorld) ShowHelp();
                return;
            }
            if (GameControls.HUDPressed && inWorld && !modalOpen && keyboardOverlay == null)
            {
                hudNavigation = !hudNavigation;
                GameControls.SetUICapture(hudNavigation);
                GameControls.BlockWorldForFrame();
                if (!hudNavigation) { ClearControllerFocus(); return; }
                controllerScope = null;
                captured = true;
            }

            if (GameControls.CancelPressed)
            {
                GameControls.BlockWorldForFrame();
                if (keyboardOverlay != null) FinishControllerKeyboard(false);
                else if (modalOpen) CloseModal();
                else if (hudNavigation)
                {
                    hudNavigation = false;
                    ClearControllerFocus();
                    GameControls.SetUICapture(false);
                }
                else if (inWorld && world.RoomId != "hub") EnterRoom("hub");
                return;
            }

            if (!captured || !controllerMode) return;
            var scope = keyboardOverlay ?? overlay ?? screen;
            if (scope == null) return;
            if (controllerScope != scope)
            {
                ClearControllerFocus();
                controllerScope = scope;
                var focused = FindControllerTarget(root.focusController?.focusedElement as VisualElement);
                var candidates = ControllerTargets(scope);
                SetControllerFocus(candidates.Contains(focused) ? focused : candidates.FirstOrDefault());
                previousNavigation = Vector2Int.zero;
            }

            var targets = ControllerTargets(scope);
            if (!targets.Contains(controllerFocus)) SetControllerFocus(targets.FirstOrDefault());
            if (keyboardOverlay != null && GameControls.DeletePressed) RemoveKeyboardCharacter();

            var direction = ControllerDirection(GameControls.Navigation);
            if (direction == Vector2Int.zero) previousNavigation = Vector2Int.zero;
            else if (direction != previousNavigation || Time.unscaledTime >= nextNavigationTime)
            {
                var first = direction != previousNavigation;
                previousNavigation = direction;
                nextNavigationTime = Time.unscaledTime + (first ? .36f : .12f);
                MoveControllerFocus(targets, direction);
            }

            if (GameControls.ConfirmPressed && controllerFocus != null)
            {
                GameControls.BlockWorldForFrame();
                if (controllerFocus is TextField field) OpenControllerKeyboard(field);
                else if (controllerFocus is Foldout foldout) foldout.value = !foldout.value;
                else if (controllerFocus is Button button)
                {
                    // Unity's Clickable handles NavigationSubmitEvent just as a pointer click.
                    // The root blocks the parallel default Input System navigation event.
                    dispatchingControllerSubmit = true;
                    try
                    {
                        using (var evt = NavigationSubmitEvent.GetPooled())
                        {
                            evt.target = button;
                            button.SendEvent(evt);
                        }
                    }
                    finally { dispatchingControllerSubmit = false; }
                }
            }
        }

        void RefreshControllerHint(bool inWorld)
        {
            if (controllerHint == null || controllerHint.parent == null)
            {
                controllerHint = Text(screen ?? root, "", "controller-hint");
                controllerHint.pickingMode = PickingMode.Ignore;
            }
            controllerHint.style.display = GameControls.Connected ? DisplayStyle.Flex : DisplayStyle.None;
            if (keyboardOverlay != null)
                controllerHint.text = GameControls.ConfirmLabel + "  Digitar     " + GameControls.DeleteLabel + "  Apagar     " + GameControls.CancelLabel + "  Cancelar";
            else if (!inWorld || modalOpen || hudNavigation)
                controllerHint.text = "Direcional  Escolher     " + GameControls.ConfirmLabel + "  Confirmar     " + GameControls.CancelLabel + "  Voltar";
            else
                controllerHint.text = "Analógico  Caminhar     " + GameControls.ConfirmLabel + "  Interagir     " + GameControls.WaveLabel + "  Acenar     " + GameControls.MenuLabel + "  Ajuda     " + GameControls.HUDLabel + "  Menu da tela";
        }

        static Vector2Int ControllerDirection(Vector2 value)
        {
            if (value.sqrMagnitude < .25f) return Vector2Int.zero;
            return Mathf.Abs(value.x) > Mathf.Abs(value.y)
                ? new Vector2Int(value.x > 0 ? 1 : -1, 0)
                : new Vector2Int(0, value.y > 0 ? 1 : -1);
        }

        static List<VisualElement> ControllerTargets(VisualElement scope)
        {
            var result = new List<VisualElement>();
            if (scope != null) CollectControllerTargets(scope, result);
            return result;
        }

        static void CollectControllerTargets(VisualElement element, List<VisualElement> result)
        {
            if (!element.enabledInHierarchy || element.resolvedStyle.display == DisplayStyle.None
                || element.resolvedStyle.visibility == Visibility.Hidden) return;
            if (element is Button)
            {
                if (!element.ClassListContains("direction")) result.Add(element);
                return;
            }
            if (element is TextField) { result.Add(element); return; }
            if (element is Foldout) result.Add(element);
            foreach (var child in element.Children()) CollectControllerTargets(child, result);
        }

        static VisualElement FindControllerTarget(VisualElement element)
        {
            for (var parent = element; parent != null; parent = parent.parent)
                if (parent is Button || parent is TextField || parent is Foldout) return parent;
            return null;
        }

        static Rect ControllerBounds(VisualElement element)
        {
            if (element is Foldout foldout)
            {
                var toggle = foldout.Q<Toggle>();
                if (toggle != null) return toggle.worldBound;
            }
            return element.worldBound;
        }

        void MoveControllerFocus(List<VisualElement> targets, Vector2Int direction)
        {
            if (targets.Count == 0) return;
            if (controllerFocus == null) { SetControllerFocus(targets[0]); return; }
            var from = ControllerBounds(controllerFocus).center;
            var axis = new Vector2(direction.x, -direction.y);
            VisualElement best = null;
            var bestScore = float.PositiveInfinity;
            foreach (var target in targets)
            {
                if (target == controllerFocus) continue;
                var bounds = ControllerBounds(target);
                if (bounds.width <= 0 || bounds.height <= 0) continue;
                var offset = bounds.center - from;
                var forward = Vector2.Dot(offset, axis);
                if (forward <= 2) continue;
                var side = Mathf.Abs(offset.x * axis.y - offset.y * axis.x);
                var score = forward + side * 3f + side * side / Mathf.Max(forward, 1);
                if (score < bestScore) { bestScore = score; best = target; }
            }
            // A single column or a scrolled layout remains fully reachable even if
            // no spatial neighbour exists in the requested direction.
            if (best == null)
            {
                var index = targets.IndexOf(controllerFocus);
                var step = direction.y > 0 || direction.x < 0 ? -1 : 1;
                best = targets[(index + step + targets.Count) % targets.Count];
            }
            SetControllerFocus(best);
        }

        void ClearControllerFocus()
        {
            if (controllerFocus != null) controllerFocus.RemoveFromClassList("controller-focus");
            controllerFocus = null;
        }

        void SetControllerFocus(VisualElement element)
        {
            ClearControllerFocus();
            controllerFocus = element;
            if (element == null) return;
            element.AddToClassList("controller-focus");
            if (!(element is TextField))
            {
                element.focusable = true;
                element.Focus();
            }
            // Focusing a TextField's inner input would launch the OS keyboard or
            // enter native text-edit mode before the player chooses to type.
            else root.Focus();
            for (var parent = element.parent; parent != null; parent = parent.parent)
                if (parent is ScrollView scroll) scroll.ScrollTo(element);
        }

        void OpenControllerKeyboard(TextField field)
        {
            if (field == null || !field.enabledInHierarchy || keyboardOverlay != null) return;
            keyboardTarget = field;
            keyboardOriginal = field.value ?? "";
            keyboardValue = keyboardOriginal;
            keyboardUpper = field == codeField;
            letterKeys.Clear();
            keyboardOverlay = Box(screen ?? root, "controller-keyboard-overlay");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ConfigureMobileScrollView(scroll);
            keyboardOverlay.Add(scroll);
            var panel = Box(scroll.contentContainer, "controller-keyboard card");
            var isName = field == nameField;
            Text(panel, isName ? "Seu nome ou apelido" : field == codeField ? "Código da turma" : "Servidor da escola", "controller-keyboard-title title");
            keyboardPreview = Text(panel, keyboardValue, "controller-keyboard-preview");
            Text(panel, "Use o direcional para escolher uma tecla e " + GameControls.ConfirmLabel + " para digitar.", "controller-keyboard-help hint");
            if (!isName) AddKeyboardRow(panel, "1234567890");
            AddKeyboardRow(panel, "qwertyuiop");
            AddKeyboardRow(panel, "asdfghjkl");
            AddKeyboardRow(panel, "zxcvbnm");
            if (isName) AddKeyboardRow(panel, "áéíóúãõçâêôàü");
            else if (field == serverField) AddKeyboardRow(panel, ":/.-_?=&%#@");
            var actions = Box(panel, "controller-keyboard-actions row");
            if (field != codeField)
            {
                Btn(actions, "Maiúsculas", ToggleKeyboardCase, "secondary controller-key");
                if (isName) Btn(actions, "Espaço", () => AppendKeyboardText(" "), "secondary controller-key");
            }
            if (isName) Btn(actions, "−", () => AppendKeyboardText("-"), "secondary controller-key");
            Btn(actions, "Apagar", RemoveKeyboardCharacter, "secondary controller-key");
            keyboardError = Text(panel, "", "controller-keyboard-error status");
            var finish = Box(panel, "controller-keyboard-actions row");
            Btn(finish, "Concluir", () => FinishControllerKeyboard(true), "primary");
            Btn(finish, "Cancelar", () => FinishControllerKeyboard(false), "quiet");
            controllerScope = null;
            ClearControllerFocus();
            GameControls.SetUICapture(true);
            GameControls.BlockWorldForFrame();
        }

        void AddKeyboardRow(VisualElement parent, string characters)
        {
            var row = Box(parent, "controller-keyboard-row row");
            foreach (var character in characters)
            {
                var key = character.ToString();
                var button = Btn(row, keyboardUpper ? key.ToUpperInvariant() : key, () => AppendKeyboardText(keyboardUpper ? key.ToUpperInvariant() : key), "controller-key");
                button.userData = key;
                letterKeys.Add(button);
            }
        }

        void ToggleKeyboardCase()
        {
            keyboardUpper = !keyboardUpper;
            foreach (var key in letterKeys)
            {
                var character = (string)key.userData;
                key.text = keyboardUpper ? character.ToUpperInvariant() : character;
            }
        }

        void AppendKeyboardText(string value)
        {
            if (keyboardTarget == null) return;
            if (keyboardTarget == codeField) value = value.ToUpperInvariant();
            var next = keyboardValue + value;
            if (keyboardTarget == nameField) next = GameTextInputRules.NormalizeCharacters(next);
            var limit = keyboardTarget == nameField ? 20 : keyboardTarget.maxLength;
            if (limit > 0 && next.Length > limit)
            {
                keyboardError.text = "Limite de " + limit + " caracteres.";
                return;
            }
            keyboardValue = next;
            keyboardPreview.text = keyboardValue;
            keyboardError.text = "";
        }

        void RemoveKeyboardCharacter()
        {
            keyboardValue = GameTextInputRules.RemoveLastCharacter(keyboardValue);
            if (keyboardPreview != null) keyboardPreview.text = keyboardValue;
            if (keyboardError != null) keyboardError.text = "";
        }

        void FinishControllerKeyboard(bool accept)
        {
            if (keyboardOverlay == null) return;
            if (accept && keyboardTarget == nameField && !GameTextInputRules.IsValidPlayerName(keyboardValue))
            {
                keyboardError.text = "Use de 2 a 20 letras, números, espaços ou hífen.";
                return;
            }
            var target = keyboardTarget;
            if (accept && target == nameField) keyboardValue = GameTextInputRules.NormalizePlayerName(keyboardValue);
            if (target != null) target.value = accept ? keyboardValue : keyboardOriginal;
            keyboardOverlay.RemoveFromHierarchy();
            keyboardOverlay = null;
            keyboardTarget = null;
            keyboardPreview = null;
            keyboardError = null;
            letterKeys.Clear();
            controllerScope = overlay ?? screen;
            SetControllerFocus(target);
            GameControls.BlockWorldForFrame();
        }

        bool SuppressNativeControllerNavigation => !dispatchingControllerSubmit
            && (controllerMode || GameControls.ControllerActivity)
            && Keyboard.current?.anyKey.isPressed != true;

        void SuppressControllerMove(NavigationMoveEvent evt)
        {
            if (!SuppressNativeControllerNavigation) return;
            evt.StopImmediatePropagation();
            root.focusController?.IgnoreEvent(evt);
        }

        void SuppressControllerSubmit(NavigationSubmitEvent evt)
        {
            if (!SuppressNativeControllerNavigation) return;
            evt.StopImmediatePropagation();
            root.focusController?.IgnoreEvent(evt);
        }

        void SuppressControllerCancel(NavigationCancelEvent evt)
        {
            if (!SuppressNativeControllerNavigation) return;
            evt.StopImmediatePropagation();
            root.focusController?.IgnoreEvent(evt);
        }

        void ControllerPointerDown(PointerDownEvent evt)
        {
            controllerMode = false;
            ClearControllerFocus();
            if (keyboardOverlay == null) hudNavigation = false;
        }

        void ControllerKeyDown(KeyDownEvent evt)
        {
            if (keyboardOverlay != null)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    controllerEscapeFrame = Time.frameCount;
                    FinishControllerKeyboard(false);
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) FinishControllerKeyboard(true);
                else if (evt.keyCode == KeyCode.Backspace) RemoveKeyboardCharacter();
                else if (!char.IsControl(evt.character)) AppendKeyboardText(evt.character.ToString());
                else return;
                evt.StopImmediatePropagation();
                root.focusController?.IgnoreEvent(evt);
                return;
            }
            controllerMode = false;
            hudNavigation = false;
            ClearControllerFocus();
        }
    }
}
