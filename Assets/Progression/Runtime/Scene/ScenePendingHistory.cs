using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Ked.Progression
{
    internal sealed class ScenePendingHistory
    {
        private sealed class ProgressionPick
        {
            public EpisodeOption Option; // 실제 진행 계산용 
            public string FromEpisodeId; // 저장 및 복원 가능한 선택 식별자
            public int SourceIndex;
            public int Anchor; // RollbackHistory와 연결되는 위치
        }

        // Rollback하면 해당 지점 이후에 봤던 Episode를 제거해야함.
        private sealed class WatchedEpisode
        {
            public string EpisodeId;
            public int Anchor;
        }

        // 지금까지 알고 있는 선택 기록 전체
        private readonly List<ProgressionPick> _picks = new();
        private readonly List<WatchedEpisode> _watched = new();
        
        private readonly List<EpisodeOption> _foldBuffer = new(); // 계산용 재사용버퍼
        
        // 선택 기록 전체 중, 현재 Scene 실행이 실제로 소비한 위치
        public int PathCursor { get; private set; }

        public bool HasRecordedChoice => PathCursor < _picks.Count;

        public int RecordedChoiceCount => _picks.Count;

        // Load 시점에 과거 선택 기록을 미리 적재
        public void RestoreChoice(
            EpisodeOption option,
            string fromEpisodeId,
            int sourceIndex)
        {
            _picks.Add(new ProgressionPick
            {
                Option = option,
                FromEpisodeId = fromEpisodeId,
                SourceIndex = sourceIndex,

                Anchor = -1,
            });
        }

        // replay하면서 하나씩 소비
        public SceneChoice TakeRecordedChoice(int anchor)
        {
            if (!HasRecordedChoice)
                throw new InvalidOperationException("자동 응답할 진행 선택 기록이 없다.");
            
            ProgressionPick pick = _picks[PathCursor++];
            
            pick.Anchor = anchor;

            return new SceneChoice(
                pick.Option,
                pick.FromEpisodeId,
                pick.SourceIndex,
                SceneChoiceSource.Recorded);
        }

        // replay 기록이 끝난 뒤 지금 새롭게 발생한 선택을 기록
        public void RecordChoice(SceneChoice choice, int anchor)
        {
            if (choice.Source == SceneChoiceSource.Recorded)
                throw new InvalidOperationException("이미 기록된 선택을 pending history에 다시 추가할 수 없다.");

            if (PathCursor != _picks.Count)
                throw new InvalidOperationException("소비되지 않은 이전 진행 선택 기록이 남아 있다.");
            
            _picks.Add(
                new ProgressionPick
                {
                    Option = choice.Option,
                    FromEpisodeId = choice.FromEpisodeId,
                    SourceIndex = choice.SourceIndex,
                    Anchor = anchor,
                });

            PathCursor = _picks.Count;

        }
    }
}
