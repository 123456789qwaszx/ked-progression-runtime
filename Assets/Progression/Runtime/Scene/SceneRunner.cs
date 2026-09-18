using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression
{
    // Scene 실행 순서를 소유하는 유일한 Runtime runner.
    // 진행 상태 계산은 SceneProgression에 위임하고 playback/replay/lifecycle 순서만 조립한다.
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
        private readonly ISceneReplayState _replayState;
        private readonly IRollbackHistory _rollbackHistory;
        private readonly IScenePersistence _persistence;
        private readonly IProgressionReporter _reporter;
        private readonly ISceneBacklog _backlog;
        private readonly IProgressionLog _log;

        public SceneRunner(
            IScenePlayback playback,
            IChapterOptionsView options,
            ISceneReplayState replayState,
            IRollbackHistory rollbackHistory,
            IScenePersistence persistence,
            IProgressionReporter reporter,
            ISceneBacklog backlog,
            IProgressionLog log = null)
        {
            _playback = playback;
            _options = options;
            _replayState = replayState;
            _rollbackHistory = rollbackHistory;
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _reporter = reporter;
            _backlog = backlog;
            _log = log ?? NullProgressionLog.Instance;
        }

        public async Task<SceneRunResult> RunAsync(
            SceneRunContext ctx,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SceneProgress progression = ctx.Progress;

            try
            {
                await EnterSceneAsync(ctx, cancellationToken);

                ApplyRestorePath(ctx, progression);

                while (true)
                {
                    SceneStepKind step =
                        await RunEpisodeStepAsync(ctx, progression, cancellationToken);

                    switch (step)
                    {
                        case SceneStepKind.Continue:
                            continue;

                        case SceneStepKind.Replay:
                            await RestartReplayAsync(ctx, progression, cancellationToken);
                            continue;

                        case SceneStepKind.SceneEnded:
                            return CommitScene(ctx, progression, SceneRunOutcome.SceneEnded);

                        case SceneStepKind.ChapterEnded:
                            return CommitScene(ctx, progression, SceneRunOutcome.ChapterEnded);

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
                throw;
            }
            catch
            {
                throw;
            }
        }

        public async Task RequestReplayAsync(SceneRunContext scene)
        {
            if (!scene.RequestReplay())
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
            SceneRunContext ctx,
            CancellationToken cancellationToken)
        {
            await _playback.BeginSceneAsync();
            _backlog.MarkSceneStart();

            cancellationToken.ThrowIfCancellationRequested();

            _persistence.EnterScene(
                ctx.Progress.Definition.ChapterId,
                ctx.Progress.SceneId,
                ctx.Progress.EntryState);

            _reporter.ReportSceneEntered(
                ctx.Progress.Definition.ChapterId,
                ctx.Progress.SceneId,
                ctx.Progress.EntryState);
        }

        private async Task<SceneStepKind> RunEpisodeStepAsync(
            SceneRunContext scene,
            SceneProgress progression,
            CancellationToken cancellationToken)
        {
            EpisodeNode episode = progression.CurrentEpisode;

            _reporter.ReportEpisodeEntered(
                scene.Progress.Definition.ChapterId,
                scene.Progress.SceneId,
                episode);

            await PlayNodeAsync(
                episode.DialogueEntryId,
                "대사",
                cancellationToken);

            if (scene.ReplayPending)
                return SceneStepKind.Replay;

            progression.NoteCurrentEpisodeWatched(_rollbackHistory.LastHistoryIndex);

            _reporter.ReportEpisodeExited(
                scene.Progress.Definition.ChapterId,
                scene.Progress.SceneId,
                episode);

            SceneChoiceResolution resolution;

            if (progression.HasRecordedChoice && _replayState.IsSeekingActive)
            {
                SceneChoice recorded =
                    progression.TakeRecordedChoice(_rollbackHistory.LastHistoryIndex);

                resolution = SceneChoiceResolution.FromChoice(recorded);
            }
            else
            {
                if (progression.HasRecordedChoice)
                    progression.DiscardUnconsumedChoices();

                if (_replayState.IsSeekingActive)
                {
                    _log.Warning(
                        "[장면] 시크 표적을 못 찾은 채 선택지에 닿았다 - 시크를 끄고 일반 재생으로 전환한다.");

                    _replayState.ClearSeek();
                }

                resolution =
                    await ResolveNextChoiceAsync(
                        scene,
                        progression,
                        episode,
                        cancellationToken);
            }

            if (scene.ReplayPending ||
                resolution.Kind == SceneChoiceResolutionKind.ReplayRequested)
            {
                return SceneStepKind.Replay;
            }

            if (resolution.Kind == SceneChoiceResolutionKind.ChapterEnded)
                return SceneStepKind.ChapterEnded;

            SceneChoice choice = resolution.Choice;

            // 커서를 옮기기 전에 기록한다 — 기록과 이동 사이에서 replay가 걸려도
            // 이미 고른 경로를 그대로 다시 따라갈 수 있어야 한다.
            if (choice.Source != SceneChoiceSource.Recorded)
                progression.RecordChoice(choice, _rollbackHistory.LastHistoryIndex);

            progression.MoveTo(choice.Option.TargetEpisodeId);

            if (!scene.Progress.Definition.IsSameScene(choice.FromEpisodeId, progression.CurrentEpisodeId))
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

            // Stop/New Game/Manual Load처럼 run 자체를 폐기하는 요청이
            // playback 대기를 깨운 직후 정상 progression으로 이어지지 않게 한다.
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task<SceneChoiceResolution> ResolveNextChoiceAsync(
            SceneRunContext ctx,
            SceneProgress progression,
            EpisodeNode episode,
            CancellationToken cancellationToken)
        {
            if (ctx.ReplayPending)
                return SceneChoiceResolution.ReplayRequested();

            ChapterAdvance advance =
                ChapterTransition.Resolve(
                    ctx.Progress.Definition,
                    progression.WorkingState);

            if (ctx.ReplayPending)
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

            if (ctx.ReplayPending)
                return SceneChoiceResolution.ReplayRequested();

            try
            {
                ResolvedOption resolved =
                    await PickAsync(advance, cancellationToken);

                if (ctx.ReplayPending)
                    return SceneChoiceResolution.ReplayRequested();

                return SceneChoiceResolution.FromChoice(
                    new SceneChoice(
                        resolved.Option,
                        episode.EpisodeId,
                        resolved.SourceIndex,
                        SceneChoiceSource.User));
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested &&
                      ctx.ReplayPending)
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
            SceneRunContext ctx,
            SceneProgress progression,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _playback.PrepareReplayAsync();

            cancellationToken.ThrowIfCancellationRequested();

            if (_rollbackHistory.TryTakeRollbackTarget(out int historyIndex))
                progression.RewindAfter(historyIndex);

            progression.RestartReplay();
            ctx.ClearReplayRequest();

            _log.Info(
                $"[장면] 리플레이 — 루트부터. " +
                $"자동 응답할 선택 {progression.RecordedChoiceCount}개");
        }

        private void ApplyRestorePath(
            SceneRunContext ctx,
            SceneProgress progression)
        {
            IReadOnlyList<ScenePathStep> path = ctx.RestorePath;

            // null은 일반 진입. 빈 path는 유효한 restore 진입이다.
            if (path == null)
                return;

            if (!progression.TryRestorePath(path))
            {
                _log.Warning(
                    "[장면] 복원 경로가 현재 챕터와 맞지 않는다 - " +
                    "경로 전체를 버리고 Scene root에서 일반 진행한다.");
                return;
            }

            // YarnChoices + line target 복원은 Host implementation이 소유한다.
            // progression path 검증이 성공한 뒤에만 시작한다.
            _replayState.BeginLoadReplay();
        }

        private SceneRunResult CommitScene(
            SceneRunContext ctx,
            SceneProgress progression,
            SceneRunOutcome outcome)
        {
            SceneCommitResult commitResult = progression.CreateCommitResult();

            _log.Info(
                $"[장면] 확정 — 선택 {commitResult.Choices.Count}개, " +
                $"시청 {commitResult.WatchedEpisodeIds.Count}개 → {commitResult.State.CurrentEpisodeId}");

            _persistence.CommitScene(
                ctx.Progress.Definition.ChapterId,
                ctx.Progress.SceneId,
                commitResult,
                outcome);

            _reporter.ReportSceneCommitted(
                ctx.Progress.Definition.ChapterId,
                ctx.Progress.SceneId,
                commitResult.Choices,
                commitResult.WatchedEpisodeIds,
                commitResult.State);

            _reporter.ReportSceneExited(
                ctx.Progress.Definition.ChapterId,
                ctx.Progress.SceneId,
                commitResult.State);

            return new SceneRunResult(outcome, commitResult.State);
        }
    }
}
