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
    /// - 왼쪽: 대사(에피소드의 줄을 하나씩) + 다음 버튼
    /// - 오른쪽: 현재 위치·스탯, 선택지, 진행 기록(장면 진입·확정)
    ///
    /// 진행 코어 쪽에서 보면 두 계약의 구현이다.
    /// - ISampleScreen — 에피소드 재생과 장면 경계 표시
    /// - IChapterOptionsView — 선택지(잠김·숨김 포함)
    /// </summary>
    public sealed class ProgressionSampleView : MonoBehaviour, ISampleScreen, IChapterOptionsView
    {
        private static Font _font;

        private SampleScript _script;
        private ChapterDefinition _chapter;

        private Text _location;
        private Text _dialogue;
        private Button _nextButton;
        private Text _stats;
        private RectTransform _choiceList;
        private Text _log;

        private readonly StringBuilder _logBuffer = new();

        private TaskCompletionSource<bool> _nextGate;
        private TaskCompletionSource<int> _choiceGate;

        public event Action RestartClicked;

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

        public void Bind(SampleScript script, ChapterDefinition chapter)
        {
            _script = script;
            _chapter = chapter;

            _location.text = chapter == null ? "챕터를 싣지 못했다" : chapter.DisplayName;
            _dialogue.text = chapter == null
                ? "챕터 JSON을 싣지 못했다. 콘솔의 진단을 볼 것."
                : "[처음부터]를 누르면 시작한다.";
        }

        public void AppendLog(string message)
        {
            _logBuffer.AppendLine(message);
            _log.text = _logBuffer.ToString();
        }

        #region ISampleScreen

        public async Task PlayEpisodeAsync(EpisodeNode episode, CancellationToken cancellationToken)
        {
            _location.text = $"{_chapter.DisplayName}  ›  장면 {episode.SceneId}  ›  {episode.Title}";

            if (!_script.TryGetLines(episode.DialogueEntryId, out IReadOnlyList<string> lines) || lines.Count == 0)
                lines = new[] { $"(대본에 '{episode.DialogueEntryId}'이(가) 없다)" };

            foreach (string line in lines)
            {
                _dialogue.text = line;
                await WaitNextAsync(cancellationToken);
            }
        }

        public void ShowSceneEntered(SceneProgress progress)
        {
            AppendLog($"▶ 장면 진입: {progress.SceneId} (루트 {progress.RootEpisodeId})");
            ShowStats(progress.EntryState);
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

            if (outcome == SceneRunOutcome.ChapterEnded)
            {
                _dialogue.text = "— 챕터 끝 —\n[처음부터]로 다시 해 볼 수 있다.";
                _nextButton.interactable = false;
            }
        }

        #endregion

        #region IChapterOptionsView

        public Task<int> ShowAsync(IReadOnlyList<ResolvedOption> options, int hiddenCount)
        {
            ClearChoices();

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

            ClearChoices();
        }

        #endregion

        private void Pick(int index)
        {
            TaskCompletionSource<int> gate = _choiceGate;
            _choiceGate = null;

            ClearChoices();
            _nextButton.interactable = true;

            gate?.TrySetResult(index);
        }

        private async Task WaitNextAsync(CancellationToken cancellationToken)
        {
            _nextGate = new TaskCompletionSource<bool>();
            _nextButton.interactable = true;

            using (cancellationToken.Register(() => _nextGate?.TrySetCanceled()))
                await _nextGate.Task;
        }

        private void ShowStats(ChapterState state)
        {
            var text = new StringBuilder();

            foreach (StatDefinition stat in _chapter.Stats)
                text.Append(stat.DisplayName).Append(' ').Append(state.GetStat(stat.Key)).Append("   ");

            _stats.text = text.ToString();
        }

        private void ClearChoices()
        {
            for (int i = _choiceList.childCount - 1; i >= 0; i--)
                Destroy(_choiceList.GetChild(i).gameObject);
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

            // 왼쪽: 대사
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

            // 오른쪽: 상태·선택지·기록
            RectTransform right = CreatePanel(root, "StatusPanel", 732f);

            Button restart = CreateButton(right, "Restart", "처음부터", 44f);
            restart.onClick.AddListener(() => RestartClicked?.Invoke());

            _stats = CreateLabel(right, "Stats", "", 22, 36f);

            CreateLabel(right, "ChoicesTitle", "선택지", 20, 30f);
            _choiceList = CreateRect(right, "Choices");
            VerticalLayoutGroup choiceLayout = _choiceList.gameObject.AddComponent<VerticalLayoutGroup>();
            choiceLayout.spacing = 6f;
            choiceLayout.childControlHeight = true;
            choiceLayout.childControlWidth = true;
            choiceLayout.childForceExpandHeight = false;
            choiceLayout.childForceExpandWidth = true;
            _choiceList.gameObject.AddComponent<LayoutElement>().minHeight = 200f;

            CreateLabel(right, "LogTitle", "진행 기록", 20, 30f);
            _log = CreateLabel(right, "Log", "", 18, 0f);
            _log.alignment = TextAnchor.UpperLeft;
            _log.GetComponent<LayoutElement>().flexibleHeight = 1f;
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
