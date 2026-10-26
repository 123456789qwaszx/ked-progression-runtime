using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 샘플 화면. 코드로 세우는 uGUI라 씬에 아무것도 놓지 않아도 된다.
    ///
    /// - 왼쪽: 위치, 대사 한 줄, [다음], 진행 기록(드라이버·저장 로그)
    /// - 오른쪽: [처음부터] [이어하기], 스탯, 선택지, 대사 기록
    ///
    /// 대사 기록의 줄은 버튼이다. 확정된 장면의 줄을 누르면 그 장면에서 갈라져 그 줄부터 다시 한다.
    /// 진행 중인 장면의 줄은 누를 수 없다(확정 전 장면 안 되감기는 연출 런타임의 롤백 몫).
    ///
    /// 진행 코어 쪽에서 보면 두 계약의 구현이다.
    /// - ISampleScreen — 대사 한 줄과 장면 경계 표시
    /// - IChapterOptionsView — 선택지(잠김·숨김 포함)
    /// </summary>
    public sealed class ProgressionSampleView : MonoBehaviour, ISampleScreen, IChapterOptionsView
    {
        private const int LogLinesShown = 10;
        private const int DialogueLinesShown = 16;
        private const int DialogueLabelLength = 38;

        private static Font _font;

        private ChapterDefinition _chapter;
        private SampleLineLog _lines;
        private Func<int, bool> _canFork;

        private Text _location;
        private Text _dialogue;
        private Button _nextButton;
        private Text _log;
        private Text _stats;
        private RectTransform _choiceList;
        private RectTransform _lineList;

        private readonly Queue<string> _logLines = new();

        private TaskCompletionSource<bool> _nextGate;
        private TaskCompletionSource<int> _choiceGate;

        public event Action RestartClicked;
        public event Action ContinueClicked;

        /// <summary>대사 기록의 줄을 눌렀다. 인자는 그 줄 번호(호스트 순번).</summary>
        public event Action<int> LineClicked;

        public static ProgressionSampleView Create(Transform owner)
        {
            EnsureEventSystem(owner);

            GameObject canvasObject = new(
                "Progression Sample Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            canvasObject.transform.SetParent(owner, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            ProgressionSampleView view = canvasObject.AddComponent<ProgressionSampleView>();
            view.Build((RectTransform)canvasObject.transform);
            return view;
        }

        public void Bind(ChapterDefinition chapter, SampleLineLog lines, Func<int, bool> canFork)
        {
            if (_lines != null)
                _lines.Changed -= RefreshLines;

            _chapter = chapter;
            _lines = lines;
            _canFork = canFork;

            if (_lines != null)
                _lines.Changed += RefreshLines;

            _location.text = chapter == null ? "챕터를 싣지 못했다" : chapter.DisplayName;
            _dialogue.text = chapter == null
                ? "챕터 JSON을 싣지 못했다. 콘솔의 진단을 볼 것."
                : "[처음부터]로 새로 시작하거나 [이어하기]로 저장된 회차를 이어한다.";

            RefreshLines();
        }

        public void AppendLog(string message)
        {
            _logLines.Enqueue(message);

            while (_logLines.Count > LogLinesShown)
                _logLines.Dequeue();

            _log.text = string.Join("\n", _logLines);
        }

        #region ISampleScreen

        public void ShowSceneEntered(SceneProgress progress)
        {
            AppendLog($"▶ 장면 진입: {progress.SceneId} (루트 {progress.RootEpisodeId})");
            ShowStats(progress.EntryState);
        }

        public void ShowLine(EpisodeNode episode, string line)
        {
            _location.text = $"{_chapter.DisplayName}  ›  장면 {episode.SceneId}  ›  {episode.Title}";
            _dialogue.text = line;
        }

        public async Task WaitNextAsync(CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource<bool>();
            _nextGate = gate;
            _nextButton.interactable = true;

            using (cancellationToken.Register(() => gate.TrySetCanceled()))
                await gate.Task;
        }

        public void ShowSceneCommitted(SceneCommitResult commit, SceneRunOutcome outcome)
        {
            // WatchedEpisodeIds는 EventKey가 붙은 에피소드만 담는다 — 보상·이벤트를 걸 자리다.
            string events = commit.WatchedEpisodeIds.Count == 0
                ? "없음"
                : string.Join(", ", commit.WatchedEpisodeIds);

            AppendLog(
                $"■ 장면 확정: {outcome} — 선택 {commit.Choices.Count}개, " +
                $"이벤트 에피소드 {events}, 다음 시작점 {commit.State.CurrentEpisodeId}");

            ShowStats(commit.State);

            // 방금 확정된 장면의 줄이 갈라질 수 있게 됐다.
            RefreshLines();

            if (outcome == SceneRunOutcome.ChapterEnded)
            {
                _dialogue.text = "— 챕터 끝 —\n대사 기록에서 지난 장면의 줄을 누르면 거기서부터 다른 길을 골라 볼 수 있다.";
                _nextButton.interactable = false;
            }
        }

        #endregion

        #region IChapterOptionsView

        public Task<int> ShowAsync(IReadOnlyList<ResolvedOption> options, int hiddenCount)
        {
            ClearChildren(_choiceList);

            _choiceGate = new TaskCompletionSource<int>();
            _nextButton.interactable = false;

            for (int i = 0; i < options.Count; i++)
            {
                ResolvedOption option = options[i];
                int index = i;

                string label = option.IsSelectable
                    ? option.Option.ChoiceLabel
                    : $"[잠김] {option.Option.ChoiceLabel} — {option.LockedReason}";

                Button button = CreateButton(_choiceList, $"Choice {i}", label, 44f);
                button.interactable = option.IsSelectable;
                button.onClick.AddListener(() => Pick(index));
            }

            if (hiddenCount > 0)
                CreateLabel(_choiceList, "Hidden", $"(조건이 맞지 않아 숨겨진 선택지 {hiddenCount}개)", 18, 30f);

            return _choiceGate.Task;
        }

        public void Cancel()
        {
            _choiceGate?.TrySetCanceled();
            _choiceGate = null;

            _nextGate?.TrySetCanceled();
            _nextGate = null;
            _nextButton.interactable = false;

            ClearChildren(_choiceList);
        }

        #endregion

        private void Pick(int index)
        {
            TaskCompletionSource<int> gate = _choiceGate;
            _choiceGate = null;

            ClearChildren(_choiceList);

            gate?.TrySetResult(index);
        }

        private void ShowStats(ChapterState state)
        {
            var text = new StringBuilder();

            foreach (StatDefinition stat in _chapter.Stats)
                text.Append(stat.DisplayName).Append(' ').Append(state.GetStat(stat.Key)).Append("   ");

            _stats.text = text.ToString();
        }

        // 대사 기록을 다시 그린다. 확정된 장면의 줄만 누를 수 있다.
        private void RefreshLines()
        {
            if (_lineList == null)
                return;

            ClearChildren(_lineList);

            if (_lines == null || _chapter == null)
                return;

            IReadOnlyList<SampleLine> entries = _lines.Entries;
            int first = Math.Max(0, entries.Count - DialogueLinesShown);

            for (int i = first; i < entries.Count; i++)
            {
                SampleLine line = entries[i];
                int serial = line.Serial;

                string scene = _chapter.TryGetNode(line.EpisodeId, out EpisodeNode node) ? node.SceneId : "?";
                string text = line.Text.Length > DialogueLabelLength
                    ? line.Text.Substring(0, DialogueLabelLength) + "…"
                    : line.Text;

                Button button = CreateButton(_lineList, $"Line {serial}", $"{serial,2}  [{scene}]  {text}", 30f);
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 16;

                button.interactable = _canFork != null && _canFork(serial);
                button.onClick.AddListener(() => LineClicked?.Invoke(serial));
            }
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void OnDestroy()
        {
            if (_lines != null)
                _lines.Changed -= RefreshLines;
        }

        #region 화면 세우기

        private void Build(RectTransform canvas)
        {
            RectTransform root = CreateRect(canvas, "Root");
            Stretch(root, 24f);

            HorizontalLayoutGroup columns = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            columns.spacing = 16f;
            columns.childControlHeight = true;
            columns.childControlWidth = true;
            columns.childForceExpandHeight = true;
            columns.childForceExpandWidth = false;

            // 왼쪽: 대사와 진행 기록
            RectTransform left = CreatePanel(root, "DialoguePanel", 1100f);
            _location = CreateLabel(left, "Location", "-", 22, 36f);
            _dialogue = CreateLabel(left, "Dialogue", "", 30, 0f);
            _dialogue.alignment = TextAnchor.UpperLeft;
            _dialogue.GetComponent<LayoutElement>().flexibleHeight = 1f;

            _nextButton = CreateButton(left, "Next", "다음 ▶", 56f);
            _nextButton.interactable = false;
            _nextButton.onClick.AddListener(() =>
            {
                TaskCompletionSource<bool> gate = _nextGate;
                _nextGate = null;
                _nextButton.interactable = false;
                gate?.TrySetResult(true);
            });

            CreateLabel(left, "LogTitle", "진행 기록", 20, 30f);
            _log = CreateLabel(left, "Log", "", 16, 240f);
            _log.alignment = TextAnchor.UpperLeft;

            // 오른쪽: 회차·상태·선택지·대사 기록
            RectTransform right = CreatePanel(root, "StatusPanel", 732f);

            RectTransform buttons = CreateRect(right, "Buttons");
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            HorizontalLayoutGroup row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = true;
            row.childForceExpandWidth = true;

            Button restart = CreateButton(buttons, "Restart", "처음부터", 44f);
            restart.onClick.AddListener(() => RestartClicked?.Invoke());

            Button resume = CreateButton(buttons, "Continue", "이어하기", 44f);
            resume.onClick.AddListener(() => ContinueClicked?.Invoke());

            _stats = CreateLabel(right, "Stats", "", 22, 36f);

            CreateLabel(right, "ChoicesTitle", "선택지", 20, 30f);
            _choiceList = CreateList(right, "Choices", 6f);
            _choiceList.gameObject.AddComponent<LayoutElement>().minHeight = 170f;

            CreateLabel(right, "LinesTitle", "대사 기록", 20, 30f);
            CreateLabel(right, "LinesHint", "확정된 장면의 줄을 누르면 그 장면에서 갈라져 그 줄부터 다시 한다.", 15, 24f);
            _lineList = CreateList(right, "Lines", 2f);
            _lineList.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private static RectTransform CreateList(Transform parent, string name, float spacing)
        {
            RectTransform list = CreateRect(parent, name);
            VerticalLayoutGroup layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return list;
        }

        private static RectTransform CreatePanel(Transform parent, string name, float width)
        {
            RectTransform panel = CreateRect(parent, name);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

            LayoutElement element = panel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleHeight = 1f;

            VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            return panel;
        }

        private static Button CreateButton(Transform parent, string name, string label, float height)
        {
            RectTransform rect = CreateRect(parent, name);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.23f, 0.29f, 1f);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateLabel(rect, "Label", label, 20, 0f);
            Stretch(text.rectTransform, 0f);
            text.alignment = TextAnchor.MiddleCenter;

            return button;
        }

        private static Text CreateLabel(Transform parent, string name, string value, int fontSize, float preferredHeight)
        {
            RectTransform rect = CreateRect(parent, name);
            Text text = rect.gameObject.AddComponent<Text>();

            text.font = Font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.text = value;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.raycastTarget = false;

            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();

            if (preferredHeight > 0f)
                layout.preferredHeight = preferredHeight;

            return text;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void EnsureEventSystem(Transform owner)
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystemObject = new("Progression Sample EventSystem", typeof(EventSystem));
            eventSystemObject.transform.SetParent(owner, false);
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private static Font Font
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _font;
            }
        }

        #endregion
    }
}
