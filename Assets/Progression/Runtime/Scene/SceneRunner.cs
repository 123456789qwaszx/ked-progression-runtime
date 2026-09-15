using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression
{
    public sealed class SceneRunner
    {
        private enum SceneStepKind
        {
            Continue,
            Replay,
            SceneEnded,
            ChapterEnded,
        }

        private readonly IScenePlayback _playback;
        private readonly IChapterOptionsView _options;
        private readonly ISceneSeek _seek;
        private readonly IRollbackHistory _rollbackHistory;
        private readonly IProgressionReporter _reporter;
        private readonly ISceneBacklog _backlog;
        private readonly IDialogueChoiceReplay _dialogueChoices;
        private readonly IProgressionLog _log;

        public SceneRunner(
            IScenePlayback playback,
            IChapterOptionsView options,
            ISceneSeek seek,
            IRollbackHistory rollbackHistory,
            IProgressionReporter reporter,
            ISceneBacklog backlog,
            IDialogueChoiceReplay dialogueChoices,
            IProgressionLog log = null)
        {
            _playback = playback ?? throw new ArgumentNullException(nameof(playback));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _seek = seek ?? throw new ArgumentNullException(nameof(seek));
            _rollbackHistory = rollbackHistory ?? throw new ArgumentNullException(nameof(rollbackHistory));
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _backlog = backlog ?? throw new ArgumentNullException(nameof(backlog));
            _dialogueChoices = dialogueChoices ?? throw new ArgumentNullException(nameof(dialogueChoices));
            _log = log ?? NullProgressionLog.Instance;
        }

        public async Task<SceneRunResult> RunAsync(
            SceneTransaction scene,
            CancellationToken cancellationToken)
        {
            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            cancellationToken.ThrowIfCancellationRequested();

            ScenePendingHistory history = scene.History;

            try
            {
                await EnterSceneAsync(scene, cancellationToken);

                ApplyLoadPlan(scene, history);
                scene.SetPhase(SceneRunPhase.LoadPlanApplied);

                while (true)
                {
                    SceneStepKind step =
                        await RunEpisodeStepAsync(scene, history, cancellationToken);

                    switch (step)
                    {
                        case SceneStepKind.Continue:
                            continue;

                        case SceneStepKind.Replay:
                            await RestartReplayAsync(scene, history, cancellationToken);
                            continue;

                        case SceneStepKind.SceneEnded:
                            return CommitScene(scene, history, SceneRunOutcome.SceneEnded);

                        case SceneStepKind.ChapterEnded:
                            return CommitScene(scene, history, SceneRunOutcome.ChapterEnded);

                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(step),
                                step,
                                "알 수 없는 장면 실행 결과다.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                scene.SetPhase(SceneRunPhase.Cancelled);
                throw;
            }
            catch
            {
                scene.SetPhase(SceneRunPhase.Faulted);
                throw;
            }
        }

        public async Task RequestReplayAsync(SceneTransaction scene)
        {
            if (scene == null || !scene.RequestReplay())
                return;

            Task stopTask = _playback.StopAsync();
            _options.Cancel();
            await stopTask;
        }

        public async Task StopAsync()
        {
            _options.Cancel();
            await _playback.StopAsync();
        }

        private async Task EnterSceneAsync(
            SceneTransaction scene,
            CancellationToken cancellationToken)
        {
            scene.SetPhase(SceneRunPhase.SceneEntering);

            await _playback.BeginSceneAsync();
            _backlog.MarkSceneStart();

            cancellationToken.ThrowIfCancellationRequested();

            scene.SetPhase(SceneRunPhase.SceneEntered);

            _reporter.ReportSceneEntered(
                scene.Chapter.ChapterId,
                scene.EntryState);

            scene.SetPhase(SceneRunPhase.EntryReported);
        }

        private async Task<SceneStepKind> RunEpisodeStepAsync(
            SceneTransaction scene,
            ScenePendingHistory history,
            CancellationToken cancellationToken)
        {
            EpisodeNode episode = scene.CurrentEpisode;

            if (episode == null)
                throw new InvalidOperationException($"에피소드 '{scene.CurrentEpisodeId}'를 찾을 수 없다.");

            scene.SetPhase(SceneRunPhase.EpisodePlaying);

            await PlayNodeAsync(
                episode.DialogueEntryId,
                "대사",
                cancellationToken);

            if (scene.ReplayPending)
                return SceneStepKind.Replay;

            history.NoteWatched(episode, _rollbackHistory.LastHistoryIndex);
            scene.SetPhase(SceneRunPhase.EpisodeCompleted);
            scene.SetPhase(SceneRunPhase.ChoiceResolving);

            SceneChoiceResolution resolution;

            if (history.HasRecordedChoice && _seek.IsSeekingActive)
            {
                SceneChoice recorded =
                    history.TakeRecordedChoice(_rollbackHistory.LastHistoryIndex);

                resolution = SceneChoiceResolution.FromChoice(recorded);
            }
            else
            {
                if (history.HasRecordedChoice)
                    history.DiscardUnconsumedChoices();

                if (_seek.IsSeekingActive)
                {
                    _log.Warning(
                        "[장면] 시크 표적을 못 찾은 채 선택지에 닿았다 - 시크를 끄고 일반 재생으로 전환한다.");

                    _seek.ClearSeek();
                }

                resolution =
                    await ResolveNextChoiceAsync(
                        scene,
                        history,
                        episode,
                        cancellationToken);
            }

            if (scene.ReplayPending
                || resolution.Kind == SceneChoiceResolutionKind.ReplayRequested)
            {
                return SceneStepKind.Replay;
            }

            if (resolution.Kind == SceneChoiceResolutionKind.ChapterEnded)
                return SceneStepKind.ChapterEnded;

            SceneChoice choice = resolution.Choice;

            if (choice.Source != SceneChoiceSource.Recorded)
                history.RecordChoice(choice, _rollbackHistory.LastHistoryIndex);

            scene.SetPhase(SceneRunPhase.ChoiceResolved);

            if (choice.Option.HasVia)
            {
                scene.SetPhase(SceneRunPhase.ViaPlaying);

                await PlayNodeAsync(
                    choice.Option.ViaNodeId,
                    "연출",
                    cancellationToken);

                if (scene.ReplayPending)
                    return SceneStepKind.Replay;
            }

            scene.MoveTo(choice.Option.TargetEpisodeId);
            scene.SetPhase(SceneRunPhase.TargetMoved);

            if (!scene.Chapter.IsSameScene(choice.FromEpisodeId, scene.CurrentEpisodeId))
                return SceneStepKind.SceneEnded;

            return SceneStepKind.Continue;
        }

        private async Task PlayNodeAsync(
            string nodeName,
            string description,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _log.Info($"[진행] {description} 시작 — \"{nodeName}\"");

            await _playback.PlayNodeAsync(nodeName);
        }

        private async Task<SceneChoiceResolution> ResolveNextChoiceAsync(
            SceneTransaction scene,
            ScenePendingHistory history,
            EpisodeNode episode,
            CancellationToken cancellationToken)
        {
            if (scene.ReplayPending)
                return SceneChoiceResolution.ReplayRequested();

            ProgressionState working =
                scene.EntryState.FoldChoices(
                    scene.Chapter,
                    history.PendingOptions());

            ChapterAdvance advance =
                ChapterTransition.Resolve(scene.Chapter, working);

            if (scene.ReplayPending)
                return SceneChoiceResolution.ReplayRequested();

            if (advance.Kind == ChapterAdvanceKind.ChapterEnded)
                return SceneChoiceResolution.ChapterEnded();

            if (advance.Kind == ChapterAdvanceKind.AutoAdvance)
            {
                ResolvedOption resolved = advance.Options[0];

                _log.Info($"[장면] 자동 간선 - {resolved.Option}");

                return SceneChoiceResolution.FromChoice(
                    new SceneChoice(
                        resolved.Option,
                        episode.EpisodeId,
                        resolved.SourceIndex,
                        SceneChoiceSource.AutoAdvance));
            }

            if (scene.ReplayPending)
                return SceneChoiceResolution.ReplayRequested();

            try
            {
                ResolvedOption resolved =
                    await PickAsync(advance, cancellationToken);

                if (scene.ReplayPending)
                    return SceneChoiceResolution.ReplayRequested();

                return SceneChoiceResolution.FromChoice(
                    new SceneChoice(
                        resolved.Option,
                        episode.EpisodeId,
                        resolved.SourceIndex,
                        SceneChoiceSource.User));
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested
                      && scene.ReplayPending)
            {
                return SceneChoiceResolution.ReplayRequested();
            }
        }

        private async Task<ResolvedOption> PickAsync(
            ChapterAdvance advance,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int picked = await _options.ShowAsync(
                advance.Options,
                advance.HiddenCount);

            cancellationToken.ThrowIfCancellationRequested();

            if (picked < 0 || picked >= advance.Options.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(picked),
                    $"선택지는 {advance.Options.Count}개인데 {picked}번이 왔다.");
            }

            ResolvedOption resolved = advance.Options[picked];

            if (!resolved.IsSelectable)
            {
                throw new InvalidOperationException(
                    $"잠긴 선택지다: [{resolved.Option.ChoiceLabel}] — " +
                    $"{resolved.BlockingCondition}");
            }

            return resolved;
        }

        private async Task RestartReplayAsync(
            SceneTransaction scene,
            ScenePendingHistory history,
            CancellationToken cancellationToken)
        {
            scene.SetPhase(SceneRunPhase.Replaying);

            cancellationToken.ThrowIfCancellationRequested();

            await _playback.PrepareReplayAsync();

            cancellationToken.ThrowIfCancellationRequested();

            if (_rollbackHistory.TryTakeRollbackTarget(out int historyIndex))
                history.RewindAfter(historyIndex);

            history.RestartReplay();
            scene.RestartFromRoot();

            _log.Info(
                $"[장면] 리플레이 — 루트부터. " +
                $"자동 응답할 선택 {history.RecordedChoiceCount}개");
        }

        private void ApplyLoadPlan(
            SceneTransaction scene,
            ScenePendingHistory history)
        {
            SceneLoadPlan plan = scene.LoadPlan;

            if (plan == null)
                return;

            if (plan.Target == null || string.IsNullOrEmpty(plan.Target.NodeName))
            {
                _log.Warning("[장면] 로드 계획에 표적이 없다 - 루트에서 시작한다.");
                return;
            }

            string cursor = scene.RootEpisodeId;

            for (int i = 0; i < plan.Path.Count; i++)
            {
                ScenePathStep step = plan.Path[i];

                if (!TryResolveSavedChoice(
                        scene.Chapter,
                        cursor,
                        step,
                        out EpisodeOption option))
                {
                    _log.Warning(
                        $"[장면] 로드 경로가 챕터와 안 맞는다 " +
                        $"({i}번째, {step.FromEpisodeId}[{step.OptionIndex}]) - " +
                        "계획을 버리고 루트에서 시작한다.");

                    history.ClearChoices();
                    return;
                }

                history.RestoreChoice(option, cursor, step.OptionIndex);
                cursor = option.TargetEpisodeId;
            }

            _dialogueChoices.RestoreChoices(plan.DialogueChoices);

            _seek.BeginLoadSeek(
                plan.Target.NodeName,
                plan.Target.LineId,
                plan.Target.Occurrence);

            _log.Info(
                $"[장면] 로드 — 루트 {scene.RootEpisodeId}에서 " +
                $"{plan.Target.NodeName}/{plan.Target.LineId}#{plan.Target.Occurrence}까지. " +
                $"경로 {history.RecordedChoiceCount}개, " +
                $"대화 선택 {plan.DialogueChoices.Count}개");
        }

        private static bool TryResolveSavedChoice(
            ChapterProgression chapter,
            string cursor,
            ScenePathStep step,
            out EpisodeOption option)
        {
            option = null;

            if (!string.Equals(step.FromEpisodeId, cursor, StringComparison.Ordinal))
                return false;

            if (!chapter.TryGetNode(cursor, out EpisodeNode episode))
                return false;

            if (step.OptionIndex < 0 || step.OptionIndex >= episode.NextOptions.Count)
                return false;

            option = episode.NextOptions[step.OptionIndex];
            return true;
        }

        private SceneRunResult CommitScene(
            SceneTransaction scene,
            ScenePendingHistory history,
            SceneRunOutcome outcome)
        {
            scene.SetPhase(SceneRunPhase.SceneCommitting);

            ProgressionState state =
                history.FoldInto(scene.Chapter, scene.EntryState);

            List<CommittedChoice> choices =
                history.CreateCommittedChoices();

            List<string> watched =
                history.CreateWatchedEpisodeIds();

            _log.Info(
                $"[장면] 확정 — 선택 {choices.Count}개, " +
                $"시청 {watched.Count}개 → {state.CurrentEpisodeId}");

            _reporter.ReportSceneCommitted(
                scene.Chapter.ChapterId,
                choices,
                watched,
                state);

            scene.SetPhase(SceneRunPhase.SceneCommitted);

            return new SceneRunResult(outcome, state);
        }
    }
}
