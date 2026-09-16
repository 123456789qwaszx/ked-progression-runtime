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
            root.sizeDelta = new Vector2(460f, 1010f);

            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

            VerticalLayoutGroup layout =
                root.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            CreateLabel(
                root,
                "Title",
                "Progression Runtime Debug",
                24,
                38f);

            CreateLabel(
                root,
                "LifecycleHeader",
                "Session / Host lifecycle",
                17,
                28f);

            CreateButton(root, "NewGame", "New Game");
            CreateButton(root, "Continue", "Continue");
            CreateButton(root, "ManualLoad", "Manual Load");
            CreateButton(root, "Stop", "Stop / Title Exit");

            CreateLabel(
                root,
                "PlaybackHeader",
                "Current playback",
                17,
                28f);

            CreateButton(
                root,
                "CompleteNode",
                "Complete Episode Node");

            CreateButton(
                root,
                "EpisodeSkip",
                "Episode Skip");

            CreateLabel(
                root,
                "ReplayHeader",
                "Scene replay",
                17,
                28f);

            CreateButton(
                root,
                "Rollback",
                "Rollback 1 Step");

            CreateButton(
                root,
                "BacklogJump",
                "Backlog Jump 2 Steps");

            Text choiceInfo = CreateLabel(
                root,
                "ChoiceInfo",
                string.Empty,
                16,
                26f);

            RectTransform choiceRoot = CreateRect(
                root,
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
                root,
                "Status",
                string.Empty,
                15,
                320f);

            status.alignment = TextAnchor.UpperLeft;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Overflow;

            choiceInfo.gameObject.SetActive(false);
            choiceRoot.gameObject.SetActive(false);

            root.gameObject.SetActive(false);

            return root.gameObject.AddComponent<ProgressionDebugUIRoot>();
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label)
        {
            RectTransform rect = CreateRect(parent, name);
            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.23f, 0.29f, 1f);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateLabel(
                rect,
                "Label",
                label,
                15,
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
