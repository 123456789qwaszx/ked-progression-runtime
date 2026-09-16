using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ked.Progression.Debugging.UI
{
    public static class ProgressionDebugUIFactory
    {
        private static Font _font;

        public static ProgressionDebugUIRoot Create(
            Transform owner,
            out RectTransform rootLayer)
        {
            EnsureEventSystem(owner);

            GameObject canvasObject = new GameObject(
                "Progression Debug Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            canvasObject.transform.SetParent(owner, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            rootLayer = CreateRect(
                canvasObject.transform,
                "RootLayer");

            Stretch(rootLayer);

            RectTransform root = CreateRect(
                rootLayer,
                "ProgressionDebugUIRoot");

            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(16f, -16f);
            root.sizeDelta = new Vector2(1580f, 1048f);

            HorizontalLayoutGroup columns =
                root.gameObject.AddComponent<HorizontalLayoutGroup>();

            columns.spacing = 12f;
            columns.childControlHeight = true;
            columns.childControlWidth = true;
            columns.childForceExpandHeight = true;
            columns.childForceExpandWidth = false;

            RectTransform consolePanel = CreatePanel(
                root,
                "ConsolePanel",
                620f);

            RectTransform controlPanel = CreatePanel(
                root,
                "ControlPanel",
                940f);

            BuildConsole(consolePanel);
            BuildControls(controlPanel);

            root.gameObject.SetActive(false);

            return root.gameObject.AddComponent<ProgressionDebugUIRoot>();
        }

        private static void BuildConsole(RectTransform parent)
        {
            CreateLabel(
                parent,
                "ConsoleHeader",
                "Runtime Console",
                24,
                38f);

            Text source = CreateLabel(
                parent,
                "ConsoleSource",
                "Target actual logs\n[LIFE] [RUN] [REPLAY] [PRESENT] [STATE]",
                14,
                44f);

            source.color = new Color(0.78f, 0.82f, 0.88f, 1f);

            Text console = CreateLabel(
                parent,
                "Console",
                string.Empty,
                14,
                0f);

            LayoutElement consoleLayout =
                console.GetComponent<LayoutElement>();

            consoleLayout.flexibleHeight = 1f;
            console.alignment = TextAnchor.UpperLeft;
            console.horizontalOverflow = HorizontalWrapMode.Wrap;
            console.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void BuildControls(RectTransform parent)
        {
            CreateLabel(
                parent,
                "Title",
                "Progression Lifecycle / Parity Debug",
                24,
                38f);

            CreateLabel(
                parent,
                "LifecycleHeader",
                "Session / Host lifecycle",
                16,
                24f);

            CreateButton(parent, "NewGame", "New Game");
            CreateButton(parent, "Continue", "Continue");
            CreateButton(parent, "ManualLoad", "Manual Load");
            CreateButton(parent, "Stop", "Stop / Title Exit");

            CreateLabel(
                parent,
                "PlaybackHeader",
                "Current playback",
                16,
                24f);

            CreateButton(
                parent,
                "CompleteNode",
                "Complete Episode Node");

            CreateButton(
                parent,
                "EpisodeSkip",
                "Episode Skip");

            CreateLabel(
                parent,
                "ReplayHeader",
                "Scene replay",
                16,
                24f);

            CreateButton(
                parent,
                "Rollback",
                "Rollback 1 Step");

            CreateButton(
                parent,
                "BacklogJump",
                "Backlog Jump 2 Steps");

            Text choiceInfo = CreateLabel(
                parent,
                "ChoiceInfo",
                string.Empty,
                15,
                24f);

            RectTransform choiceRoot = CreateRect(
                parent,
                "ChoiceRoot");

            VerticalLayoutGroup choiceLayout =
                choiceRoot.gameObject.AddComponent<VerticalLayoutGroup>();

            choiceLayout.spacing = 4f;
            choiceLayout.childControlHeight = true;
            choiceLayout.childControlWidth = true;
            choiceLayout.childForceExpandHeight = false;
            choiceLayout.childForceExpandWidth = true;

            ContentSizeFitter choiceFitter =
                choiceRoot.gameObject.AddComponent<ContentSizeFitter>();

            choiceFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement choiceLayoutElement =
                choiceRoot.gameObject.AddComponent<LayoutElement>();

            choiceLayoutElement.minHeight = 0f;

            Text status = CreateLabel(
                parent,
                "Status",
                string.Empty,
                14,
                210f);

            status.alignment = TextAnchor.UpperLeft;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;

            Text transition = CreateLabel(
                parent,
                "Transition",
                string.Empty,
                14,
                330f);

            transition.alignment = TextAnchor.UpperLeft;
            transition.horizontalOverflow = HorizontalWrapMode.Wrap;
            transition.verticalOverflow = VerticalWrapMode.Truncate;

            choiceInfo.gameObject.SetActive(false);
            choiceRoot.gameObject.SetActive(false);
        }

        private static RectTransform CreatePanel(
            Transform parent,
            string name,
            float width)
        {
            RectTransform panel = CreateRect(parent, name);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

            LayoutElement element = panel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleHeight = 1f;

            VerticalLayoutGroup layout =
                panel.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 5f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            return panel;
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label)
        {
            RectTransform rect = CreateRect(parent, name);
            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 30f;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.23f, 0.29f, 1f);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateLabel(
                rect,
                "Label",
                label,
                14,
                0f);

            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return button;
        }

        private static Text CreateLabel(
            Transform parent,
            string name,
            string value,
            int fontSize,
            float preferredHeight)
        {
            RectTransform rect = CreateRect(parent, name);
            Text text = rect.gameObject.AddComponent<Text>();

            text.font = Font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.text = value;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;

            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();

            if (preferredHeight > 0f)
                layout.preferredHeight = preferredHeight;

            return text;
        }

        private static RectTransform CreateRect(
            Transform parent,
            string name)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform));

            RectTransform rect =
                gameObject.GetComponent<RectTransform>();

            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem(Transform owner)
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystemObject = new GameObject(
                "Progression Debug EventSystem",
                typeof(EventSystem));

            eventSystemObject.transform.SetParent(owner, false);
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(
                        "LegacyRuntime.ttf");
                }

                return _font;
            }
        }
    }
}
