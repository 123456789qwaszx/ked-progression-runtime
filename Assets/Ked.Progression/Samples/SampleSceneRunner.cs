using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// ISceneRunner의 작은 구현. 장면 하나를 돌리고 확정 결과를 돌려준다.
    ///
    /// 한 장면의 순서:
    /// 0. 대사 기록에 장면 경계 표시 → 진행 저장에 장면 진입 기록
    /// 1. 에피소드의 대사를 한 줄씩 — 줄마다 대사 기록에 적고, 보이고, [다음]을 기다린다
    /// 2. 다음 간선 — 빨리 감는 중이면 저장된 선택을 다시 쓰고, 아니면 판정(챕터 끝 / 자동 간선 / 플레이어 선택)
    /// 3. 선택 기록 → 커서 이동
    /// 4. 장면 경계를 넘으면 확정(커밋)하고 돌아간다. 같은 장면이면 1로.
    ///
    /// 복원 시작(저장 경로가 있는 장면)은 "루트부터 빨리 감기"다. 장면 루트부터 대사를 화면 없이 기록에만 적으며
    /// 지나가고, 저장된 간선 선택을 다시 쓰고, 표적 줄에 닿으면 평소처럼 보이기 시작한다.
    /// 실제 게임(ked-presentation-runtime)의 줄 시크와 같은 모양이다.
    ///
    /// 하지 않는 것: 진행 중 장면 안의 되감기(롤백). 그것은 연출 런타임의 몫이고,
    /// 갖춘 구현은 ked-presentation-runtime의 ProgressionHost/SceneRunner다.
    /// </summary>
    public sealed class SampleSceneRunner : ISceneRunner
    {
        private readonly ISampleScreen _screen;
        private readonly IChapterOptionsView _options;
        private readonly SampleScript _script;
        private readonly SampleLineLog _lines;
        private readonly IScenePersistence _persistence;
        private readonly IProgressionLog _log;

        // 다음 복원 장면에서 빨리 감아 갈 장면 안 줄 번호. 시작 전에 정한다.
        private int? _pendingSeekLine;

        // 지금 빨리 감는 중이면 표적 줄.
        private int? _seekLine;

        /// <summary>대사 한 줄이 화면에 보였다. 인자는 장면 안 줄 번호. 이어하기 지점 갱신에 쓴다.</summary>
        public event Action<int> LineShown;

        public SampleSceneRunner(
            ISampleScreen screen,
            IChapterOptionsView options,
            SampleScript script,
            SampleLineLog lines,
            IScenePersistence persistence = null,
            IProgressionLog log = null)
        {
            _screen = screen;
            _options = options;
            _script = script;
            _lines = lines;
            _persistence = persistence;
            _log = log;
        }

        public bool IsFastForwarding => _seekLine.HasValue;

        /// <summary>다음에 저장 경로로 여는 장면에서 장면 안 몇 번째 줄까지 빨리 감을지. null이면 루트에서.</summary>
        public void PrepareRestore(int? lineInScene) => _pendingSeekLine = lineInScene;

        // 진행 코어가 선택·시청 기록에 붙이는 위치 표시. 장면 안 마지막 줄 번호를 넣는다 —
        // 실제 게임이 롤백 기록의 색인을 넣는 것과 같은 좌표다.
        private int Anchor => _lines.LastLineInScene;

        public async Task<SceneRunResult> RunAsync(
            SceneRunSession session,
            CancellationToken cancellationToken)
        {
            SceneProgress progress = session.Progress;

            _lines.MarkSceneBoundary();
            _persistence?.EnterScene(progress.Definition.ChapterId, progress.EntryState);

            _seekLine = session.StartsFromRestore ? _pendingSeekLine : null;
            _pendingSeekLine = null;

            _screen.ShowSceneEntered(progress);

            if (_seekLine.HasValue)
                _log?.Info($"[샘플] 빨리 감기 — 장면 '{progress.SceneId}' 루트부터 장면 안 {_seekLine.Value}번 줄까지");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EpisodeNode episode = progress.CurrentEpisode;

                await PlayEpisodeAsync(episode, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                progress.NoteCurrentEpisodeWatched(Anchor);

                SceneChoice choice;

                if (_seekLine.HasValue && progress.HasRecordedChoice)
                {
                    // 빨리 감는 중 — 저장된 선택을 다시 쓴다. 다시 기록하지 않는다.
                    choice = progress.TakeRecordedChoice(Anchor);
                }
                else
                {
                    // 여기부터는 평소 진행. 다 쓰지 못한 저장 선택은 더 이상 유효하지 않다.
                    if (progress.HasRecordedChoice)
                        progress.DiscardUnconsumedChoices();

                    if (_seekLine.HasValue)
                    {
                        _log?.Warning("[샘플] 표적 줄에 닿기 전에 저장된 선택이 바닥났다 — 여기서부터 평소대로 진행한다.");
                        _seekLine = null;
                    }

                    ChapterAdvance advance =
                        ChapterTransition.Resolve(
                            progress.Definition,
                            progress.EffectiveState);

                    if (advance.Kind == ChapterAdvanceKind.ChapterEnded)
                        return Commit(progress, SceneRunOutcome.ChapterEnded);

                    choice = advance.Kind == ChapterAdvanceKind.AutoAdvance
                        ? ToChoice(advance.Options[0], episode, SceneChoiceSource.AutoAdvance)
                        : ToChoice(await PickAsync(advance, cancellationToken), episode, SceneChoiceSource.User);

                    progress.RecordChoice(choice, Anchor);
                }

                progress.AdvanceTo(choice.Option.TargetEpisodeId);

                if (!progress.Definition.IsSameScene(choice.FromEpisodeId, progress.CurrentEpisodeId))
                    return Commit(progress, SceneRunOutcome.SceneEnded);
            }
        }

        // 이 샘플에는 장면 안 되감기가 없다. 화면도 그것을 요청하지 않는다.
        public Task RequestReplayAsync(SceneRunSession scene) => Task.CompletedTask;

        public Task StopAsync()
        {
            _options.Cancel();
            _screen.Cancel();
            return Task.CompletedTask;
        }

        private async Task PlayEpisodeAsync(EpisodeNode episode, CancellationToken cancellationToken)
        {
            if (!_script.TryGetLines(episode.DialogueEntryId, out IReadOnlyList<string> lines) || lines.Count == 0)
                lines = new[] { $"(대본에 '{episode.DialogueEntryId}'이(가) 없다)" };

            foreach (string line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int lineInScene = _lines.Add(episode.EpisodeId, line);

                // 빨리 감기 — 기록에만 적고 지나간다.
                if (_seekLine.HasValue && lineInScene < _seekLine.Value)
                    continue;

                _seekLine = null;

                _screen.ShowLine(episode, line);
                LineShown?.Invoke(lineInScene);

                await _screen.WaitNextAsync(cancellationToken);
            }
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

            _persistence?.CommitScene(progress.Definition.ChapterId, commit, outcome);
            _screen.ShowSceneCommitted(commit, outcome);

            return new SceneRunResult(outcome, commit.State);
        }
    }
}
