using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ked.Progression.Debugging.UI
{
    public sealed class ProgressionDebugUIRoot
        : UIRoot<ProgressionDebugUIRoot.Refs>
    {
        public readonly struct ChoiceItemData
        {
            public int Index { get; }
            public string Label { get; }
            public bool IsInteractable { get; }

            public ChoiceItemData(
                int index,
                string label,
                bool isInteractable)
            {
                Index = index;
                Label = label;
                IsInteractable = isInteractable;
            }
        }

        public enum Refs
        {
            NewGame,
            Continue,
            ManualLoad,
            Stop,
            CompleteNode,
            EpisodeSkip,
            Rollback,
            BacklogJump,
            BacklogPreviousScene,
            ChoiceInfo,
            ChoiceRoot,
            Status,
            Transition,
            Console,
        }

        public event Action NewGameClicked;
        public event Action ContinueClicked;
        public event Action ManualLoadClicked;
        public event Action StopClicked;
        public event Action CompleteNodeClicked;
        public event Action EpisodeSkipClicked;
        public event Action RollbackClicked;
        public event Action BacklogJumpClicked;
        public event Action BacklogPreviousSceneClicked;
        public event Action<int> ChoiceClicked;

        private RectTransform _choiceRoot;
        private Text _choiceInfo;
        private Text _status;
        private Text _transition;
        private Text _console;

        private readonly List<GameObject> _choiceItems = new List<GameObject>();
        private readonly List<string> _consoleLines = new List<string>();

        private const int ConsoleLineLimit = 80;

        protected override void OnInitialize()
        {
            BindEvent(View.Button(Refs.NewGame), HandleNewGameClicked);
            BindEvent(View.Button(Refs.Continue), HandleContinueClicked);
            BindEvent(View.Button(Refs.ManualLoad), HandleManualLoadClicked);
            BindEvent(View.Button(Refs.Stop), HandleStopClicked);
            BindEvent(View.Button(Refs.CompleteNode), HandleCompleteNodeClicked);
            BindEvent(View.Button(Refs.EpisodeSkip), HandleEpisodeSkipClicked);
            BindEvent(View.Button(Refs.Rollback), HandleRollbackClicked);
            BindEvent(View.Button(Refs.BacklogJump), HandleBacklogJumpClicked);
            BindEvent(
                View.Button(Refs.BacklogPreviousScene),
                HandleBacklogPreviousSceneClicked);

            _choiceInfo = View.Text(Refs.ChoiceInfo);
            _choiceRoot = View.Rect(Refs.ChoiceRoot);
            _status = View.Text(Refs.Status);
            _transition = View.Text(Refs.Transition);
            _console = View.Text(Refs.Console);

            Application.logMessageReceived += HandleUnityLog;
        }

        protected override void OnDestroy()
        {
            Application.logMessageReceived -= HandleUnityLog;
            base.OnDestroy();
        }

        public void SetState(ProgressionDebugSnapshot snapshot)
        {
            if (_status == null)
                return;

            _status.text =
                "Target / actual progression\n" +
                "--------------------------------\n" +
                $"Running  : {snapshot.IsRunning}\n" +
                $"Chapter  : {snapshot.ChapterId}\n" +
                $"Scene    : {snapshot.SceneId}\n" +
                $"Episode  : {snapshot.EpisodeId}\n" +
                $"Node     : {snapshot.NodeName}\n" +
                $"History  : {snapshot.HistoryIndex}\n" +
                $"Pending  : {snapshot.PendingCount}\n" +
                $"Seeking  : {snapshot.IsSeeking}\n" +
                $"Saved Ep : {snapshot.SavedEpisodeId}\n\n" +
                "Target = ked-progression-runtime/dev\n" +
                "실제 ProgressionDriver / SceneRunner 실행값";
        }

        public void SetTransitionReport(ProgressionDebugComparisonReport report)
        {
            if (_transition == null)
                return;

            if (report == null)
            {
                _transition.text =
                    "Reference parity\n" +
                    ProgressionDebugReferenceRules.Reference +
                    "\n\n버튼을 누르면 typed parity row를 표시한다.\n" +
                    "Snapshot = Target 실제값 / Rules = Reference 불변 계약";
                return;
            }

            var lines = new List<string>
            {
                "Reference parity",
                report.ReferenceSource,
                string.Empty,
                report.Title,
                "--------------------------------",
            };

            for (int i = 0; i < report.Rows.Count; i++)
            {
                ProgressionDebugComparisonRow row = report.Rows[i];

                lines.Add(
                    $"[{StatusLabel(row.Status)}]" +
                    $"[{EvidenceLabel(row.Evidence)}]" +
                    $"[{OwnerLabel(row.Owner)}] {row.Topic}");
                lines.Add($"  Target: {row.Target}");
                lines.Add($"  Ref   : {row.Reference}");

                if (i + 1 < report.Rows.Count)
                    lines.Add(string.Empty);
            }

            _transition.text = string.Join("\n", lines.ToArray());
        }

        public void SetChoices(
            IReadOnlyList<ChoiceItemData> items,
            int hiddenCount)
        {
            ClearChoices();

            bool hasChoices = items != null && items.Count > 0;

            if (_choiceInfo != null)
            {
                _choiceInfo.gameObject.SetActive(hasChoices);
                _choiceInfo.text = hasChoices
                    ? $"Choice (visible={items.Count}, hidden={hiddenCount})"
                    : string.Empty;
            }

            if (_choiceRoot == null)
                return;

            _choiceRoot.gameObject.SetActive(hasChoices);

            if (!hasChoices)
                return;

            for (int i = 0; i < items.Count; i++)
            {
                ChoiceItemData item = items[i];

                Button button = ProgressionDebugUIFactory.CreateButton(
                    _choiceRoot,
                    $"Choice_{item.Index}",
                    item.Label);

                button.interactable = item.IsInteractable;

                UI_EventHandler handler =
                    button.gameObject.AddComponent<UI_EventHandler>();

                int capturedIndex = item.Index;
                handler.OnClickHandler += _ =>
                    ChoiceClicked?.Invoke(capturedIndex);

                _choiceItems.Add(button.gameObject);
            }
        }

        private void HandleUnityLog(
            string condition,
            string stackTrace,
            LogType type)
        {
            string prefix;

            switch (type)
            {
                case LogType.Warning:
                    prefix = "WARN ";
                    break;

                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    prefix = "ERROR ";
                    break;

                default:
                    prefix = string.Empty;
                    break;
            }

            _consoleLines.Add(prefix + condition);

            if (_consoleLines.Count > ConsoleLineLimit)
            {
                _consoleLines.RemoveRange(
                    0,
                    _consoleLines.Count - ConsoleLineLimit);
            }

            if (_console != null)
                _console.text = string.Join("\n", _consoleLines.ToArray());
        }

        private void ClearChoices()
        {
            for (int i = 0; i < _choiceItems.Count; i++)
            {
                if (_choiceItems[i] != null)
                    Destroy(_choiceItems[i]);
            }

            _choiceItems.Clear();
        }

        private static string StatusLabel(ProgressionDebugParityStatus status)
        {
            switch (status)
            {
                case ProgressionDebugParityStatus.Match:
                    return "MATCH";
                case ProgressionDebugParityStatus.Diff:
                    return "DIFF";
                case ProgressionDebugParityStatus.OutsideProgression:
                    return "OUTSIDE-PROGRESSION";
                case ProgressionDebugParityStatus.ProgressionOnly:
                    return "PROGRESSION-ONLY";
                default:
                    return status.ToString().ToUpperInvariant();
            }
        }

        private static string EvidenceLabel(ProgressionDebugParityEvidence evidence)
        {
            switch (evidence)
            {
                case ProgressionDebugParityEvidence.Verified:
                    return "VERIFIED";
                case ProgressionDebugParityEvidence.HarnessGap:
                    return "HARNESS-GAP";
                case ProgressionDebugParityEvidence.CharacterizationNeeded:
                    return "CHARACTERIZATION";
                default:
                    return evidence.ToString().ToUpperInvariant();
            }
        }

        private static string OwnerLabel(ProgressionDebugParityOwner owner)
        {
            return owner.ToString().ToUpperInvariant();
        }

        private void HandleNewGameClicked(PointerEventData _) =>
            NewGameClicked?.Invoke();

        private void HandleContinueClicked(PointerEventData _) =>
            ContinueClicked?.Invoke();

        private void HandleManualLoadClicked(PointerEventData _) =>
            ManualLoadClicked?.Invoke();

        private void HandleStopClicked(PointerEventData _) =>
            StopClicked?.Invoke();

        private void HandleCompleteNodeClicked(PointerEventData _) =>
            CompleteNodeClicked?.Invoke();

        private void HandleEpisodeSkipClicked(PointerEventData _) =>
            EpisodeSkipClicked?.Invoke();

        private void HandleRollbackClicked(PointerEventData _) =>
            RollbackClicked?.Invoke();

        private void HandleBacklogJumpClicked(PointerEventData _) =>
            BacklogJumpClicked?.Invoke();

        private void HandleBacklogPreviousSceneClicked(PointerEventData _) =>
            BacklogPreviousSceneClicked?.Invoke();
    }
}
