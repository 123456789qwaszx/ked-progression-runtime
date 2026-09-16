using System;
using System.Threading.Tasks;

namespace Ked.Progression
{
    // Chapter 하나의 실행 순서를 소유한다.
    //
    // 이 클래스가 정하는 것은 "언제" 경계를 여닫는가이다.
    // 각 경계에서 Yarn 변수, Backlog, 무대, RollbackHistory, 저장 등을
    // 실제로 무엇으로 처리할지는 I*Boundary 구현이 담당한다.
    public sealed class ChapterSession
    {
        private readonly ProgressionBoundaries _boundaries;

        private ChapterAdvance _pendingAdvance;
        private bool _hasPendingAdvance;

        public ChapterProgression Chapter { get; }
        public ProgressionState State { get; private set; }
        public SceneProgression Scene { get; private set; }

        public bool IsStarted { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool IsWaitingForAdvance => _hasPendingAdvance;

        public ChapterSession(
            ChapterProgression chapter,
            ProgressionBoundaries boundaries = null)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            _boundaries = boundaries ?? new ProgressionBoundaries();
        }

        // 새 Chapter면 ChapterProgression의 초기 상태를 사용한다.
        // 이어하기면 저장에서 복원한 Chapter 상태를 그대로 진입 상태로 사용한다.
        public async Task EnterAsync(ProgressionState restoredState = null)
        {
            if (IsStarted)
                throw new InvalidOperationException("Chapter는 두 번 시작할 수 없다.");

            State = restoredState ?? Chapter.CreateEntryState();

            ChapterEntryKind entryKind = restoredState == null
                ? ChapterEntryKind.New
                : ChapterEntryKind.Restore;

            await _boundaries.Chapter.EnterAsync(
                new ChapterEnterContext(Chapter, State, entryKind));

            await EnterSceneAsync(State);

            IsStarted = true;
        }

        // 현재 Episode의 재생이 끝났음을 Progression에 알린다.
        // 여기서 watched를 기록하고 다음 진행 가능성을 계산한 뒤 Episode Exit 경계를 닫는다.
        // 선택/자동 간선을 실제로 타는 것은 AdvanceAsync에서 수행한다.
        public async Task<ChapterAdvance> CompleteCurrentEpisodeAsync(int rollbackAnchor = -1)
        {
            RequireRunning();

            if (_hasPendingAdvance)
                throw new InvalidOperationException("이전 Episode의 진행 결과를 아직 처리하지 않았다.");

            EpisodeNode episode = Scene.CurrentEpisode;

            Scene.NoteCurrentEpisodeWatched(rollbackAnchor);

            ChapterAdvance advance = ChapterTransition.Resolve(
                Chapter,
                Scene.WorkingState);

            await _boundaries.Episode.ExitAsync(
                new EpisodeExitContext(Scene, episode, advance));

            if (advance.Kind == ChapterAdvanceKind.ChapterEnded)
            {
                await ExitSceneAsync();

                await _boundaries.Chapter.ExitAsync(
                    new ChapterExitContext(Chapter, State));

                IsCompleted = true;
                return advance;
            }

            _pendingAdvance = advance;
            _hasPendingAdvance = true;
            return advance;
        }

        // CompleteCurrentEpisodeAsync가 확정한 선택지 중 하나를 실제로 진행한다.
        // 같은 Scene이면 다음 Episode만 연다.
        // Scene이 바뀌면 이전 Scene을 먼저 Commit/Exit한 뒤 새 Scene과 Episode를 연다.
        public async Task AdvanceAsync(
            ResolvedOption selected,
            SceneChoiceSource source,
            int rollbackAnchor = -1)
        {
            RequireRunning();

            if (!_hasPendingAdvance)
                throw new InvalidOperationException("먼저 현재 Episode를 완료해야 한다.");

            if (!IsPendingOption(selected))
                throw new ArgumentException("현재 Episode에서 확정한 진행 선택지가 아니다.", nameof(selected));

            string fromEpisodeId = Scene.CurrentEpisodeId;
            string targetEpisodeId = selected.Option.TargetEpisodeId;
            bool sameScene = Chapter.IsSameScene(fromEpisodeId, targetEpisodeId);

            Scene.Advance(selected, source, rollbackAnchor);
            _hasPendingAdvance = false;

            if (sameScene)
            {
                await _boundaries.Episode.EnterAsync(
                    new EpisodeEnterContext(Scene, Scene.CurrentEpisode));
                return;
            }

            await ExitSceneAsync();
            await EnterSceneAsync(State);
        }

        private async Task EnterSceneAsync(ProgressionState entryState)
        {
            Scene = new SceneProgression(Chapter, entryState);

            await _boundaries.Scene.EnterAsync(
                new SceneEnterContext(Scene));

            await _boundaries.Episode.EnterAsync(
                new EpisodeEnterContext(Scene, Scene.CurrentEpisode));
        }

        private async Task ExitSceneAsync()
        {
            ProgressionState committedState = Scene.Commit();

            await _boundaries.Scene.ExitAsync(
                new SceneExitContext(Scene, committedState));

            State = committedState;
        }

        private bool IsPendingOption(ResolvedOption selected)
        {
            if (!selected.IsSelectable)
                return false;

            for (int i = 0; i < _pendingAdvance.Options.Count; i++)
            {
                ResolvedOption pending = _pendingAdvance.Options[i];

                if (pending.SourceIndex == selected.SourceIndex &&
                    ReferenceEquals(pending.Option, selected.Option) &&
                    pending.IsSelectable)
                {
                    return true;
                }
            }

            return false;
        }

        private void RequireRunning()
        {
            if (!IsStarted)
                throw new InvalidOperationException("Chapter가 아직 시작되지 않았다.");

            if (IsCompleted)
                throw new InvalidOperationException("이미 종료된 Chapter다.");
        }
    }
}
