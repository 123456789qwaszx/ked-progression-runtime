using System.Collections.Generic;

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

        public bool HasRecordedChoice =>
            PathCursor < _picks.Count;

        public int RecordedChoiceCount =>
            _picks.Count;
    }
}
