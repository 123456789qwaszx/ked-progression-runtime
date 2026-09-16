using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // Scene 하나의 순수 진행 상태.
    //
    // - EntryState는 Scene 진입 시점의 확정 상태로 고정한다.
    // - Scene 안의 선택/시청 기록은 pending history에 쌓는다.
    // - WorkingState는 EntryState + 현재까지 소비한 pending choice로 계산한다.
    // - replay/rollback은 pending과 cursor만 되감고 Scene 자체를 교체하지 않는다.
    // - 정상 Scene 완료에서만 Commit하여 다음 Scene의 EntryState를 만든다.
    //
    // 이 클래스는 playback, Task, Unity/Yarn lifecycle을 모른다.
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

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _history.CreatePendingPath();

        public bool IsCommitted => _committed;
        public bool HasRecordedChoice => _history.HasRecordedChoice;
        public int RecordedChoiceCount => _history.RecordedChoiceCount;

        public SceneProgression(
            ChapterProgression chapter,
            ProgressionState entryState)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            EntryState = entryState ?? throw new ArgumentNullException(nameof(entryState));

            if (!chapter.TryGetNode(entryState.CurrentEpisodeId, out EpisodeNode root))
            {
                throw new ArgumentException(
                    $"Scene 진입 에피소드 '{entryState.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(entryState));
            }

            RootEpisodeId = root.EpisodeId;
            CurrentEpisodeId = root.EpisodeId;
            SceneId = root.SceneId;
        }

        public void NoteCurrentEpisodeWatched(int rollbackAnchor)
        {
            RequireOpen();
            _history.NoteWatched(CurrentEpisode, rollbackAnchor);
        }

        // 실제로 선택된 간선을 pending history에 기록한다.
        // Via 재생 전에도 replay path를 보존해야 하므로 cursor 이동과 분리한다.
        public void RecordChoice(SceneChoice choice, int rollbackAnchor)
        {
            RequireOpen();

            if (!string.Equals(
                    choice.FromEpisodeId,
                    CurrentEpisodeId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"선택의 출발점 '{choice.FromEpisodeId}'가 현재 Episode '{CurrentEpisodeId}'와 다르다.",
                    nameof(choice));
            }

            RequireCurrentOption(choice.Option, choice.SourceIndex);
            _history.RecordChoice(choice, rollbackAnchor);
        }

        // playback/Via가 끝난 뒤 Runtime이 실제 Episode cursor를 이동시킨다.
        public void MoveTo(string episodeId)
        {
            RequireOpen();

            if (!Chapter.TryGetNode(episodeId, out _))
            {
                throw new ArgumentException(
                    $"이동할 Episode '{episodeId}'가 챕터 '{Chapter.ChapterId}'에 없다.",
                    nameof(episodeId));
            }

            CurrentEpisodeId = episodeId;
        }

        // Core 테스트나 단순 호출자를 위한 원자적 편의 API.
        // Runtime SceneRunner는 Via 재생 순서를 보존하기 위해 RecordChoice/MoveTo를 나눠 사용한다.
        public void Advance(
            ResolvedOption selected,
            SceneChoiceSource source,
            int rollbackAnchor)
        {
            RequireOpen();

            if (!selected.IsSelectable)
                throw new ArgumentException("잠긴 선택지는 진행에 사용할 수 없다.", nameof(selected));

            var choice = new SceneChoice(
                selected.Option,
                CurrentEpisodeId,
                selected.SourceIndex,
                source);

            RecordChoice(choice, rollbackAnchor);
            MoveTo(selected.Option.TargetEpisodeId);
        }

        // Host save의 progression path만 받아 현재 Chapter graph에 맞는지 검증하고
        // root부터 다시 소비할 recorded choice로 적재한다.
        // 하나라도 맞지 않으면 부분 경로를 남기지 않고 root 일반 진행으로 되돌린다.
        public bool TryRestorePath(IReadOnlyList<ScenePathStep> path)
        {
            RequireOpen();

            if (path == null)
                throw new ArgumentNullException(nameof(path));

            _history.ClearChoices();
            CurrentEpisodeId = RootEpisodeId;

            string cursor = RootEpisodeId;

            for (int i = 0; i < path.Count; i++)
            {
                ScenePathStep step = path[i];

                if (!string.Equals(step.FromEpisodeId, cursor, StringComparison.Ordinal) ||
                    !Chapter.TryGetNode(cursor, out EpisodeNode episode) ||
                    step.OptionIndex < 0 ||
                    step.OptionIndex >= episode.NextOptions.Count)
                {
                    _history.ClearChoices();
                    CurrentEpisodeId = RootEpisodeId;
                    return false;
                }

                EpisodeOption option = episode.NextOptions[step.OptionIndex];
                _history.RestoreChoice(option, cursor, step.OptionIndex);
                cursor = option.TargetEpisodeId;
            }

            _history.RestartReplay();
            CurrentEpisodeId = RootEpisodeId;
            return true;
        }

        // Load/replay에서 저장된 선택 하나를 다시 소비한다.
        // history cursor만 전진시키고 실제 Episode cursor 이동은 Runtime이 Via 처리 뒤 수행한다.
        public SceneChoice TakeRecordedChoice(int rollbackAnchor)
        {
            RequireOpen();

            SceneChoice choice = _history.TakeRecordedChoice(rollbackAnchor);

            if (!string.Equals(CurrentEpisodeId, choice.FromEpisodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Recorded choice의 출발점 '{choice.FromEpisodeId}'가 현재 Episode '{CurrentEpisodeId}'와 다르다.");
            }

            RequireCurrentOption(choice.Option, choice.SourceIndex);
            return choice;
        }

        public void DiscardUnconsumedChoices()
        {
            RequireOpen();
            _history.DiscardUnconsumedChoices();
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

        // 정상 Scene 완료에서 한 번만 호출한다.
        // Core는 확정 결과를 계산할 뿐 save/report는 Runtime/Host가 처리한다.
        public SceneCommitResult Commit()
        {
            RequireOpen();

            var result = new SceneCommitResult(
                WorkingState,
                _history.CreateCommittedChoices(),
                _history.CreateWatchedEpisodeIds());

            _committed = true;
            return result;
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
                throw new InvalidOperationException("이미 commit된 Scene은 변경할 수 없다.");
        }
    }
}
