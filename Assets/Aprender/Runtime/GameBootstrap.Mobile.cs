using UnityEngine;
using UnityEngine.UIElements;

namespace Aprender
{
    public sealed partial class GameBootstrap
    {
        [SerializeField, Tooltip("Prévia do layout de iPhone no Editor; não altera a plataforma do build.")]
        bool previewIPhoneLayout;

        [SerializeField, Tooltip("Margens da prévia em pixels: esquerda, topo, direita e base. Use zero para uma tela sem recorte.")]
        Vector4 previewSafeAreaInsets = new Vector4(44, 0, 44, 21);

        PanelSettings mobilePanel;
        VisualElement mobileSafeArea;
        TextField activeMobileField;
        Label mobileServerError;
        TouchStick mobileStick;
        Rect lastMobileSafeRect = new Rect(-1, -1, -1, -1);
        Vector2Int lastMobileResolution;
        Vector2 lastMobilePanelSize;
        bool mobileApplicationActive = true;
        bool mobileApplicationPaused;
        bool lastKeyboardVisible;

        public bool IsMobileUI { get; private set; }

        // Call after document.panelSettings is assigned and before reading rootVisualElement.
        void InitializeMobileUI()
        {
            IsMobileUI = Application.isMobilePlatform || (Application.isEditor && previewIPhoneLayout);
            if (world != null) world.UseMobileFraming = IsMobileUI;
            if (!IsMobileUI || document == null || document.panelSettings == null) return;
            // A runtime copy keeps desktop/editor asset settings unchanged.
            mobilePanel = Instantiate(document.panelSettings);
            mobilePanel.name = "Aprender iPhone (runtime)";
            mobilePanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            mobilePanel.scale = MobilePixelScale();
            document.panelSettings = mobilePanel;
        }

        float MobilePixelScale()
        {
            // UI Toolkit units are pixels before panel scaling. Account for iPhone's
            // physical pixel density instead of shrinking 52-unit touch targets to 20pt.
            var dpi = Screen.dpi;
            if (!Application.isEditor && dpi >= 160 && dpi <= 1000) return Mathf.Clamp(dpi / 150f, 1, 6);
            return Mathf.Max(1, Mathf.Min(Screen.width, Screen.height) / 360f);
        }

        // Call before root.Clear(), so captured touches never survive a screen change.
        void ResetMobileUI()
        {
            mobileStick?.Dispose();
            mobileStick = null;
            activeMobileField = null;
            mobileServerError = null;
            mobileSafeArea = null;
            lastMobileSafeRect = new Rect(-1, -1, -1, -1);
            if (world != null) world.TouchMove = Vector2.zero;
        }

        // Call immediately after screen = Box(root, "screen"). Existing overlays
        // already belong to screen, so they inherit these same safe bounds.
        void ConfigureMobileScreen()
        {
            root.EnableInClassList("mobile", IsMobileUI);
            root.EnableInClassList("phone", IsMobileUI);
            root.EnableInClassList("online-session", session != null && session.IsOnline);
            if (!IsMobileUI) return;
            mobileSafeArea = new VisualElement { name = "phone-safe-area" };
            mobileSafeArea.AddToClassList("safe-area");
            mobileSafeArea.style.position = Position.Absolute;
            screen.RemoveFromHierarchy();
            root.Add(mobileSafeArea);
            mobileSafeArea.Add(screen);
            root.schedule.Execute(() => RefreshMobileSafeArea(true));
        }

        // Call at the beginning of Update, including while the login screen is open.
        void UpdateMobileUI()
        {
            if (!IsMobileUI || root == null) return;
            var resolution = new Vector2Int(Screen.width, Screen.height);
            var panelSize = new Vector2(root.resolvedStyle.width, root.resolvedStyle.height);
            if (resolution != lastMobileResolution || panelSize != lastMobilePanelSize)
            {
                lastMobileResolution = resolution;
                lastMobilePanelSize = panelSize;
                if (mobilePanel != null) mobilePanel.scale = MobilePixelScale();
                lastMobileSafeRect = new Rect(-1, -1, -1, -1);
            }
            RefreshMobileSafeArea(false);
            SetMobileControlsEnabled(mobileApplicationActive && !mobileApplicationPaused && !modalOpen
                && !busy && session != null && session.IsJoined && world != null && world.InputEnabled
                && !GameControls.UICaptured);
        }

        void RefreshMobileSafeArea(bool force)
        {
            if (mobileSafeArea == null || root.panel == null || Screen.width <= 0 || Screen.height <= 0
                || !(root.resolvedStyle.width > 0) || !(root.resolvedStyle.height > 0)) return;
            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            if (Application.isEditor && previewIPhoneLayout)
            {
                var inset = previewSafeAreaInsets;
                safe = Rect.MinMaxRect(Mathf.Clamp(inset.x, 0, Screen.width * .4f),
                    Mathf.Clamp(inset.w, 0, Screen.height * .4f),
                    Screen.width - Mathf.Clamp(inset.z, 0, Screen.width * .4f),
                    Screen.height - Mathf.Clamp(inset.y, 0, Screen.height * .4f));
            }
            var keyboardVisible = TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible;
            root.EnableInClassList("keyboard-open", keyboardVisible);
            if (keyboardVisible)
            {
                var keyboard = TouchScreenKeyboard.area;
                // Some native keyboards report zero area during their opening animation.
                // Reserve a temporary lower section, then use the measured area next frame.
                var keyboardTop = keyboard.height > 0 ? keyboard.yMax : Screen.height * .48f;
                var bottom = Mathf.Clamp(keyboardTop, safe.yMin, safe.yMax - 48);
                safe = Rect.MinMaxRect(safe.xMin, bottom, safe.xMax, safe.yMax);
            }
            if (!force && safe == lastMobileSafeRect && keyboardVisible == lastKeyboardVisible) return;
            lastMobileSafeRect = safe;
            lastKeyboardVisible = keyboardVisible;
            // Screen's origin is bottom-left, UI Toolkit's is top-left. Convert both
            // corners through the panel so this also works with PanelSettings scaling.
            var topLeft = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(safe.xMin, Screen.height - safe.yMax));
            var bottomRight = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(safe.xMax, Screen.height - safe.yMin));
            mobileSafeArea.style.left = Mathf.Max(0, topLeft.x);
            mobileSafeArea.style.top = Mathf.Max(0, topLeft.y);
            mobileSafeArea.style.right = Mathf.Max(0, root.resolvedStyle.width - bottomRight.x);
            mobileSafeArea.style.bottom = Mathf.Max(0, root.resolvedStyle.height - bottomRight.y);
            root.EnableInClassList("short-screen", bottomRight.y - topLeft.y < 380);
            ScrollMobileFieldIntoView();
        }

        void ConfigureMobileTextField(TextField field, TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default)
        {
            if (!IsMobileUI || field == null) return;
            field.keyboardType = keyboardType;
            field.autoCorrection = false;
            field.hideSoftKeyboard = false;
            field.hideMobileInput = false;
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                activeMobileField = field;
                ScrollMobileFieldIntoView();
            });
        }

        void ConfigureMobileScrollView(ScrollView scroll)
        {
            if (!IsMobileUI) return;
            // Scrolling uses a finger drag on iPhone; desktop scrollbars otherwise
            // consume space and can extend into the notch margins.
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        void ScrollMobileFieldIntoView()
        {
            var field = activeMobileField;
            if (field == null || field.panel == null) return;
            var scroll = field.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) return;
            // Schedule after safe-area and native-keyboard geometry has been applied.
            field.schedule.Execute(() =>
            {
                if (field.panel != null && field.enabledInHierarchy) scroll.ScrollTo(field);
            }).ExecuteLater(60);
        }

        // Call only in Join(), after ValidateName(), before disabling the screen.
        // Practice never needs a server address and must stay available offline.
        bool ValidateMobileServerAddress()
        {
            if (mobileServerError != null) mobileServerError.style.display = DisplayStyle.None;
            if (!IsMobileUI || !System.Uri.TryCreate((enteredServer ?? "").Trim(), System.UriKind.Absolute, out var uri))
                return true; // Existing GameSession validation explains malformed URLs.
            if (!uri.IsLoopback && uri.Host != "0.0.0.0" && uri.Host != "::" && uri.Host != "[::]") return true;
            const string message = "No iPhone, use o endereço do Mac ou da escola. "
                + "localhost é o próprio iPhone. Exemplo: http://meu-mac.local:8787";
            if (status != null) status.text = message;
            if (serverField != null)
            {
                for (var ancestor = serverField.parent; ancestor != null; ancestor = ancestor.parent)
                    if (ancestor is Foldout foldout) foldout.value = true;
                if (mobileServerError == null)
                    mobileServerError = Text(serverField.parent, message, "status");
                mobileServerError.style.display = DisplayStyle.Flex;
                serverField.Focus();
                activeMobileField = serverField;
                ScrollMobileFieldIntoView();
            }
            return false;
        }

        // Return true on mobile; otherwise the caller may create its desktop D-pad.
        bool CreateMobileControls(VisualElement parent)
        {
            if (!IsMobileUI) return false;
            mobileStick?.Dispose();
            mobileStick = new TouchStick(value =>
            {
                if (world != null) world.TouchMove = value;
            });
            parent.Add(mobileStick);
            return true;
        }

        void SetMobileControlsEnabled(bool enabled)
        {
            if (mobileStick != null && mobileStick.InputEnabled != enabled) mobileStick.InputEnabled = enabled;
            if (!enabled && world != null) world.TouchMove = Vector2.zero;
        }

        // Call these from the host's existing application lifecycle callbacks.
        void SetMobileApplicationFocus(bool focused)
        {
            mobileApplicationActive = focused;
            if (!focused) SetMobileControlsEnabled(false);
        }

        void SetMobileApplicationPaused(bool paused)
        {
            mobileApplicationPaused = paused;
            if (paused) SetMobileControlsEnabled(false);
        }

        void DisposeMobileUI()
        {
            ResetMobileUI();
            if (mobilePanel != null) Destroy(mobilePanel);
            mobilePanel = null;
        }
    }
}
