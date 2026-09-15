using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // Scene 하나의 진행 수명.
    //
    // EntryState는 Scene 진입 시점의 확정 상태로 고정된다.
    // Scene 안에서 발생한 선택은 PendingHistory에 쌓고 WorkingState로만 계산한다.
    // Scene을 빠져나갈 때 Commit하여 다음 Scene의 EntryState를 만든다.
    public sealed class SceneProgression
    {
        private readonly ScenePendingHistory _history = new();
        private bool _committed;

        public ChapterProgression Chapter { get; }
        public ProgressionState EntryState { get; }

        public string SceneId { get; }
        public string RootEpisodeId { get; }
        public string CurrentEpisodeId { get; private set; }

        public EpisodeNode RootEpisode => GetEpisode(RootEpisodeId);
        public EpisodeNode CurrentEpisode => GetEpisode(CurrentEpisodeId);

        public ProgressionState WorkingState =>
            _history.FoldInto(Chapter, EntryState);

        public bool IsCommitted => _committed;

        public SceneProgression(
            ChapterProgression chapter,
            ProgressionState entryState)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            EntryState = entryState ?? throw new ArgumentNullException(nameof(entryState));

            if (!chapter.TryGetNode(entryState.CurrentEpisodeId, out EpisodeNode root))
                throw new ArgumentException(
                    $"Scene 진입 에피소드 '{entryState.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(entryState));

            RootEpisodeId = root.EpisodeId;
            CurrentEpisodeId = root.EpisodeId;
            SceneId = root.SceneId;
        }

        public void NoteCurrentEpisodeWatched(int rollbackAnchor)
        {
            RequireOpen();
            _history.NoteWatched(CurrentEpisode, rollbackAnchor);
        }

        public void Advance(
            ResolvedOption selected,
            SceneChoiceSource source,
            int rollbackAnchor)
        {
            RequireOpen();

            if (!selected.IsSelectable)
                throw new ArgumentException("잠긴 선택지는 진행에 사용할 수 없다.", nameof(selected));

            RequireCurrentOption(selected.Option, selected.SourceIndex);

            _history.RecordChoice(
                new SceneChoice(
                    selected.Option,
                    CurrentEpisodeId,
                    selected.SourceIndex,
                    source),
                rollbackAnchor);

            CurrentEpisodeId = selected.Option.TargetEpisodeId;
        }

        public void RewindAfter(int rollbackAnchor)
        {
            RequireOpen();

            _history.RewindAfter(rollbackAnchor);
            CurrentEpisodeId = WorkingState.CurrentEpisodeId;
        }

        public void RestartReplay()
        {
            RequireOpen();

            _history.RestartReplay();
            CurrentEpisodeId = RootEpisodeId;
        }

        // Scene 종료 시 한 번만 호출한다.
        // 반환값은 다음 Scene의 EntryState가 된다.
        public ProgressionState Commit()
        {
            RequireOpen();

            ProgressionState committed = WorkingState;
            _committed = true;
            return committed;
        }

        private void RequireCurrentOption(EpisodeOption option, int sourceIndex)
        {
            IReadOnlyList<EpisodeOption> options = CurrentEpisode.NextOptions;

            if (sourceIndex < 0 || sourceIndex >= options.Count ||
                !ReferenceEquals(options[sourceIndex], option))
            {
                throw new ArgumentException(
                    $"선택지가 현재 에피소드 '{CurrentEpisodeId}'의 간선이 아니다.",
                    nameof(option));
            }
        }

        private EpisodeNode GetEpisode(string episodeId)
        {
            if (Chapter.TryGetNode(episodeId, out EpisodeNode episode))
                return episode;

            throw new InvalidOperationException(
                $"에피소드 '{episodeId}'가 챕터 '{Chapter.ChapterId}'에 없다.");
        }

        private void RequireOpen()
        {
            if (_committed)
                throw new InvalidOperationException("이미 종료된 Scene은 변경할 수 없다.");
        }
    }
}
