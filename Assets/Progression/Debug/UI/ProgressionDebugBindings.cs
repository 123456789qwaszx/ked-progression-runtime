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

            _view.NewGameClicked += _host.RequestNewGame;
            _view.ContinueClicked += _host.RequestContinue;
            _view.ManualLoadClicked += _host.RequestManualLoad;
            _view.StopClicked += _host.RequestStop;
            _view.CompleteNodeClicked += _host.RequestCompleteNode;
            _view.EpisodeSkipClicked += _host.RequestEpisodeSkip;
            _view.RollbackClicked += _host.RequestRollback;
            _view.BacklogJumpClicked += _host.RequestBacklogJump;
            _view.ChoiceClicked += _host.SelectChoice;

            _host.StateChanged += _view.SetState;
            _host.ChoicesChanged += HandleChoicesChanged;

            _view.SetState(_host.CurrentSnapshot);
            HandleChoicesChanged(
                _host.CurrentOptions,
                _host.HiddenChoiceCount);
        }

        public void Dispose()
        {
            if (!_bound)
                return;

            _bound = false;

            _view.NewGameClicked -= _host.RequestNewGame;
            _view.ContinueClicked -= _host.RequestContinue;
            _view.ManualLoadClicked -= _host.RequestManualLoad;
            _view.StopClicked -= _host.RequestStop;
            _view.CompleteNodeClicked -= _host.RequestCompleteNode;
            _view.EpisodeSkipClicked -= _host.RequestEpisodeSkip;
            _view.RollbackClicked -= _host.RequestRollback;
            _view.BacklogJumpClicked -= _host.RequestBacklogJump;
            _view.ChoiceClicked -= _host.SelectChoice;

            _host.StateChanged -= _view.SetState;
            _host.ChoicesChanged -= HandleChoicesChanged;
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
