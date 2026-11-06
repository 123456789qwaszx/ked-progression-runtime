using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // Scene 하나의 순수 진행 상태.
    //
    // - EntryState는 Scene 진입 시점의 확정 상태로 고정한다.
    // - Scene 안의 선택/시청 기록은 pending history에 쌓는다.
    // - EffectiveState는 EntryState에 현재까지 소비한 pending choice를 적용해 계산한다.
    // - replay/rollback은 pending과 cursor만 되감고 Scene 자체를 교체하지 않는다.
    // - 정상 Scene 완료에서만 Commit하여 다음 Scene의 EntryState를 만든다.
    //
    // 이 클래스는 playback, Task, Unity/Yarn lifecycle을 모른다.
    public sealed class SceneProgress
    {
        private readonly ScenePendingHistory _pendingHistory = new();

        public ChapterDefinition Definition { get; }
        public ChapterState EntryState { get; }

        public string SceneId { get; }
        public string RootEpisodeId { get; }
        public string CurrentEpisodeId { get; private set; }

        public EpisodeNode CurrentEpisode => GetEpisode(CurrentEpisodeId);

        public ChapterState EffectiveState =>
            _pendingHistory.ApplyTo(Definition, EntryState);

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _pendingHistory.CreatePendingPath();

        public bool HasRecordedChoice => _pendingHistory.HasRecordedChoice;
        public int RecordedChoiceCount => _pendingHistory.RecordedChoiceCount;

        public SceneProgress(
            ChapterDefinition definition,
            ChapterState entryState)
        {
            Definition = definition;
            EntryState = entryState;

            EpisodeNode root = GetEpisode(entryState.CurrentEpisodeId);

            RootEpisodeId = root.EpisodeId;
            CurrentEpisodeId = root.EpisodeId;
            SceneId = root.SceneId;
        }

        #region 현재 진행
        
        public void NoteCurrentEpisodeWatched(int rollbackAnchor)
        { 
            _pendingHistory.NoteWatched(CurrentEpisode, rollbackAnchor);
        }
        
        // 실제로 선택된 간선을 pending history에 기록한다.
        public void RecordChoice(SceneChoice choice, int rollbackAnchor)
        { 
            _pendingHistory.RecordChoice(choice, rollbackAnchor);
        }

        // 선택 기록 후 호출자가 실제 Episode cursor를 옮긴다.
        public void AdvanceTo(string episodeId)
        { 
            CurrentEpisodeId = episodeId;
        }
        
        # endregion
        
        #region 저장 경로 복원과 소비
        
        // 저장된 Scene 선택 경로를 recorded choice로 복원.
        // - empty: Scene root에서 시작
        // - non-empty: root부터 저장된 선택 경로를 다시 소비
        public void RestorePath(IReadOnlyList<ScenePathStep> path)
        {
            _pendingHistory.ClearChoices();

            string cursor = RootEpisodeId;

            for (int i = 0; i < path.Count; i++)
            {
                ScenePathStep step = path[i];

                EpisodeNode episode = GetEpisode(cursor);
                EpisodeOption option = episode.NextOptions[step.OptionIndex];

                _pendingHistory.RestoreChoice(
                    option,
                    cursor,
                    step.OptionIndex);

                cursor = option.TargetEpisodeId;
            }

            ResetForReplay();
        }

        // Load/replay에서 저장된 선택 하나를 다시 소비한다.
        // history cursor만 전진시키고 실제 Episode cursor 이동은 Runtime이 뒤이어 수행한다.
        public SceneChoice TakeRecordedChoice(int rollbackAnchor)
        {
            return _pendingHistory.TakeRecordedChoice(rollbackAnchor);
        }

        public void DiscardUnconsumedChoices()
        {
            _pendingHistory.DiscardUnconsumedChoices();
        }
        
        #endregion
        
        #region Rollback과 Replay

        // rollbackAnchor 이후의 pending 기록을 지운 다음,
        // 그 결과에 맞춰 실제 Episode 커서를 다시 맞춘다
        // Scene 진입점은 유지하면서 Scene 내부 진행만 되감는 것
        public void RollbackTo(int rollbackAnchor)
        {
            _pendingHistory.TruncateAfter(rollbackAnchor);
            CurrentEpisodeId = EffectiveState.CurrentEpisodeId;
        }

        public void ResetForReplay()
        {
            _pendingHistory.ResetRecordedChoiceCursor();
            CurrentEpisodeId = RootEpisodeId;
        }
        
        #endregion
        
        #region Scene 확정

        // 정상 Scene 완료 시 Runtime이 확정에 사용할 결과를 만든다.
        public SceneCommitResult CreateCommitResult()
        {
            var result = new SceneCommitResult(
                EffectiveState,
                _pendingHistory.CreateCommittedChoices(),
                _pendingHistory.CreateWatchedEpisodeIds());

            return result;
        }

        private EpisodeNode GetEpisode(string episodeId)
        {
            if (Definition.TryGetNode(episodeId, out EpisodeNode episode))
                return episode;

            throw new InvalidOperationException(
                $"에피소드 '{episodeId}'가 챕터 '{Definition.ChapterId}'에 없다.");
        }
        
        #endregion
    }
}
