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

        public ChapterDefinition Definition { get; }
        public ProgressionState EntryState { get; }

        public string SceneId { get; }
        public string RootEpisodeId { get; }
        public string CurrentEpisodeId { get; private set; }

        public EpisodeNode RootEpisode => GetEpisode(RootEpisodeId);
        public EpisodeNode CurrentEpisode => GetEpisode(CurrentEpisodeId);

        public ProgressionState WorkingState =>
            _history.FoldInto(Definition, EntryState);

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _history.CreatePendingPath();

        public bool HasRecordedChoice => _history.HasRecordedChoice;
        public int RecordedChoiceCount => _history.RecordedChoiceCount;

        public SceneProgression(
            ChapterDefinition definition,
            ProgressionState entryState)
        {
            Definition = definition;
            EntryState = entryState;

            definition.TryGetNode(entryState.CurrentEpisodeId, out EpisodeNode root);
            
            RootEpisodeId = root.EpisodeId;
            CurrentEpisodeId = root.EpisodeId;
            SceneId = root.SceneId;
        }

        public void NoteCurrentEpisodeWatched(int rollbackAnchor) => 
            _history.NoteWatched(CurrentEpisode, rollbackAnchor);
        
        // 실제로 선택된 간선을 pending history에 기록한다.
        // Via 재생 전에도 replay path를 보존해야 하므로 cursor 이동과 분리한다.
        public void RecordChoice(SceneChoice choice, int rollbackAnchor) =>
            _history.RecordChoice(choice, rollbackAnchor);
        
        // playback/Via가 끝난 뒤 Runtime이 실제 Episode cursor를 이동시킨다.
        public void MoveTo(string episodeId) => CurrentEpisodeId = episodeId;
        
        // 저장된 Scene 선택 경로가 현재 Chapter 그래프에서도 여전히 유효한지 검사하고,
        // 유효하면 그 경로를 “root부터 다시 소비할 recorded choice로”으로 복원 및 적재
        public bool TryRestorePath(IReadOnlyList<ScenePathStep> path)
        {
            // 버전 이슈로 복원경로와 현재 챕터 구조 불일치
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            _history.ClearChoices();
            CurrentEpisodeId = RootEpisodeId;

            string cursor = RootEpisodeId;

            for (int i = 0; i < path.Count; i++)
            {
                ScenePathStep step = path[i];

                if (!string.Equals(step.FromEpisodeId, cursor, StringComparison.Ordinal) ||
                    !Definition.TryGetNode(cursor, out EpisodeNode episode) ||
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
            SceneChoice choice = _history.TakeRecordedChoice(rollbackAnchor);

            if (!string.Equals(CurrentEpisodeId, choice.FromEpisodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Recorded choice의 출발점 '{choice.FromEpisodeId}'가 현재 Episode '{CurrentEpisodeId}'와 다르다.");
            }

            return choice;
        }

        public void DiscardUnconsumedChoices()
        {
            _history.DiscardUnconsumedChoices();
        }

        // rollbackAnchor 이후의 pending 기록을 지운 다음,
        // 그 결과에 맞춰 실제 Episode 커서를 다시 맞춘다
        // Scene 진입점은 유지하면서 Scene 내부 진행만 되감는 것
        public void RewindAfter(int rollbackAnchor)
        {
            _history.TruncateAfter(rollbackAnchor);
            CurrentEpisodeId = WorkingState.CurrentEpisodeId;
        }

        public void RestartReplay()
        {
            _history.RestartReplay();
            CurrentEpisodeId = RootEpisodeId;
        }

        // 정상 Scene 완료 시 Runtime이 확정에 사용할 결과를 만든다.
        public SceneCommitResult CreateCommitResult()
        {
            var result = new SceneCommitResult(
                WorkingState,
                _history.CreateCommittedChoices(),
                _history.CreateWatchedEpisodeIds());

            return result;
        }

        private EpisodeNode GetEpisode(string episodeId)
        {
            if (Definition.TryGetNode(episodeId, out EpisodeNode episode))
                return episode;

            throw new InvalidOperationException(
                $"에피소드 '{episodeId}'가 챕터 '{Definition.ChapterId}'에 없다.");
        }
        
        #region Test
        
        // Core 테스트나 단순 호출자를 위한 원자적 편의 API.
        // Runtime SceneRunner는 Via 재생 순서를 보존하기 위해 RecordChoice/MoveTo를 나눠 사용한다.
        public void Advance(ResolvedOption selected, SceneChoiceSource source, int rollbackAnchor)
        {
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
        
        #endregion
    }
}
