using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// ISceneRunner의 최소 구현. 장면 하나를 처음부터 끝까지 돌리고 확정 결과를 돌려준다.
    ///
    /// 한 장면의 순서:
    /// 1. 에피소드 재생 → 시청 표시
    /// 2. 다음 간선 판정(ChapterTransition.Resolve) — 챕터 끝 / 자동 간선 / 플레이어 선택
    /// 3. 선택 기록 → 커서 이동
    /// 4. 장면 경계를 넘으면 확정(커밋)하고 돌아간다. 같은 장면이면 1로.
    ///
    /// 하지 않는 것: 리플레이·롤백·저장·복원. 그것을 모두 갖춘 구현은
    /// ked-presentation-runtime의 ProgressionHost/SceneRunner다.
    /// </summary>
    public sealed class SampleSceneRunner : ISceneRunner
    {
        private readonly ISampleScreen _screen;
        private readonly IChapterOptionsView _options;

        // 진행 코어가 선택 기록에 붙이는 위치 표시.
        // 롤백이 있는 호스트는 대사 이력의 색인을 넣는다. 이 샘플은 시청한 에피소드 수를 넣는다.
        private int _watchedCount;

        public SampleSceneRunner(
            ISampleScreen screen,
            IChapterOptionsView options)
        {
            _screen = screen;
            _options = options;
        }

        public async Task<SceneRunResult> RunAsync(
            SceneRunSession session,
            CancellationToken cancellationToken)
        {
            if (session.StartsFromRestore)
                throw new NotSupportedException("이 샘플은 저장·복원을 하지 않는다 — 항상 새로 시작한다.");

            SceneProgress progress = session.Progress;

            _screen.ShowSceneEntered(progress);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EpisodeNode episode = progress.CurrentEpisode;

                await _screen.PlayEpisodeAsync(episode, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                progress.NoteCurrentEpisodeWatched(++_watchedCount);

                ChapterAdvance advance =
                    ChapterTransition.Resolve(
                        progress.Definition,
                        progress.EffectiveState);

                if (advance.Kind == ChapterAdvanceKind.ChapterEnded)
                    return Commit(progress, SceneRunOutcome.ChapterEnded);

                SceneChoice choice = advance.Kind == ChapterAdvanceKind.AutoAdvance
                    ? ToChoice(advance.Options[0], episode, SceneChoiceSource.AutoAdvance)
                    : ToChoice(await PickAsync(advance, cancellationToken), episode, SceneChoiceSource.User);

                progress.RecordChoice(choice, _watchedCount);
                progress.AdvanceTo(choice.Option.TargetEpisodeId);

                if (!progress.Definition.IsSameScene(choice.FromEpisodeId, progress.CurrentEpisodeId))
                    return Commit(progress, SceneRunOutcome.SceneEnded);
            }
        }

        // 이 샘플에는 리플레이가 없다. 화면도 그것을 요청하지 않는다.
        public Task RequestReplayAsync(SceneRunSession scene) => Task.CompletedTask;

        public Task StopAsync()
        {
            _options.Cancel();
            _screen.Cancel();
            return Task.CompletedTask;
        }

        private async Task<ResolvedOption> PickAsync(
            ChapterAdvance advance,
            CancellationToken cancellationToken)
        {
            int picked = await _options.ShowAsync(advance.Options, advance.HiddenCount);

            cancellationToken.ThrowIfCancellationRequested();

            if (picked < 0 || picked >= advance.Options.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(picked),
                    $"선택지는 {advance.Options.Count}개인데 {picked}번이 왔다.");
            }

            ResolvedOption resolved = advance.Options[picked];

            // 잠긴 선택지는 화면이 누를 수 없게 그린다. 그래도 왔다면 화면의 잘못이다.
            if (!resolved.IsSelectable)
            {
                throw new InvalidOperationException(
                    $"잠긴 선택지다: [{resolved.Option.ChoiceLabel}] — {resolved.BlockingCondition}");
            }

            return resolved;
        }

        private static SceneChoice ToChoice(
            ResolvedOption resolved,
            EpisodeNode from,
            SceneChoiceSource source)
        {
            return new SceneChoice(
                resolved.Option,
                from.EpisodeId,
                resolved.SourceIndex,
                source);
        }

        private SceneRunResult Commit(SceneProgress progress, SceneRunOutcome outcome)
        {
            SceneCommitResult commit = progress.CreateCommitResult();

            _screen.ShowSceneCommitted(commit, outcome);

            return new SceneRunResult(outcome, commit.State);
        }
    }
}
