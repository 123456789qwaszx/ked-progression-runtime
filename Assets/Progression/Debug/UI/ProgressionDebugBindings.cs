using System;
using System.Collections.Generic;

namespace Ked.Progression.Debugging.UI
{
    public sealed class ProgressionDebugBindings : IDisposable
    {
        private readonly ProgressionDebugHost _host;
        private readonly ProgressionDebugUIRoot _view;

        private bool _bound;

        public ProgressionDebugBindings(
            ProgressionDebugHost host,
            ProgressionDebugUIRoot view)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void Bind()
        {
            if (_bound)
                return;

            _bound = true;

            _view.NewGameClicked += HandleNewGameClicked;
            _view.ContinueClicked += HandleContinueClicked;
            _view.ManualLoadClicked += HandleManualLoadClicked;
            _view.StopClicked += HandleStopClicked;
            _view.CompleteNodeClicked += HandleCompleteNodeClicked;
            _view.EpisodeSkipClicked += HandleEpisodeSkipClicked;
            _view.RollbackClicked += HandleRollbackClicked;
            _view.BacklogJumpClicked += HandleBacklogJumpClicked;
            _view.ChoiceClicked += _host.SelectChoice;

            _host.StateChanged += _view.SetState;
            _host.ChoicesChanged += HandleChoicesChanged;

            _view.SetState(_host.CurrentSnapshot);
            _view.SetTransitionReport(
                "Reference parity\n" +
                ProgressionDebugReferenceRules.Reference +
                "\n\n버튼을 누르면 Target의 실제 상태와 Reference 규칙의 차이를 여기에 표시한다.");

            HandleChoicesChanged(
                _host.CurrentOptions,
                _host.HiddenChoiceCount);
        }

        public void Dispose()
        {
            if (!_bound)
                return;

            _bound = false;

            _view.NewGameClicked -= HandleNewGameClicked;
            _view.ContinueClicked -= HandleContinueClicked;
            _view.ManualLoadClicked -= HandleManualLoadClicked;
            _view.StopClicked -= HandleStopClicked;
            _view.CompleteNodeClicked -= HandleCompleteNodeClicked;
            _view.EpisodeSkipClicked -= HandleEpisodeSkipClicked;
            _view.RollbackClicked -= HandleRollbackClicked;
            _view.BacklogJumpClicked -= HandleBacklogJumpClicked;
            _view.ChoiceClicked -= _host.SelectChoice;

            _host.StateChanged -= _view.SetState;
            _host.ChoicesChanged -= HandleChoicesChanged;
        }

        private void HandleNewGameClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.NewGame);
            _host.RequestNewGame();
        }

        private void HandleContinueClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.Continue);
            _host.RequestContinue();
        }

        private void HandleManualLoadClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.ManualLoad);
            _host.RequestManualLoad();
        }

        private void HandleStopClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.Stop);
            _host.RequestStop();
        }

        private void HandleCompleteNodeClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.CompleteNode);
            _host.RequestCompleteNode();
        }

        private void HandleEpisodeSkipClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.EpisodeSkip);
            _host.RequestEpisodeSkip();
        }

        private void HandleRollbackClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.Rollback);
            _host.RequestRollback();
        }

        private void HandleBacklogJumpClicked()
        {
            Show(ProgressionDebugReferenceRules.Transition.BacklogJump);
            _host.RequestBacklogJump();
        }

        private void Show(ProgressionDebugReferenceRules.Transition transition)
        {
            _view.SetTransitionReport(
                ProgressionDebugReferenceRules.Describe(transition));
        }

        private void HandleChoicesChanged(
            IReadOnlyList<ResolvedOption> options,
            int hiddenCount)
        {
            var items =
                new List<ProgressionDebugUIRoot.ChoiceItemData>(
                    options?.Count ?? 0);

            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    ResolvedOption option = options[i];

                    string label =
                        string.IsNullOrEmpty(option.Option.ChoiceLabel)
                            ? $"Choice {i}"
                            : $"{i}: {option.Option.ChoiceLabel}";

                    items.Add(
                        new ProgressionDebugUIRoot.ChoiceItemData(
                            i,
                            label,
                            option.IsSelectable));
                }
            }

            _view.SetChoices(items, hiddenCount);
        }
    }
}
