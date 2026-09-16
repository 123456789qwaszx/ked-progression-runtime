using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // Scene의 pending 진행을 정상 완료 시점에 외부로 투영한 결과.
    // Core는 이 값까지만 계산하고 save/report 실행은 Runtime/Host가 담당한다.
    public sealed class SceneCommitResult
    {
        public ProgressionState State { get; }
        public IReadOnlyList<CommittedChoice> Choices { get; }
        public IReadOnlyList<string> WatchedEpisodeIds { get; }

        public SceneCommitResult(
            ProgressionState state,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Choices = choices ?? throw new ArgumentNullException(nameof(choices));
            WatchedEpisodeIds = watchedEpisodeIds ?? throw new ArgumentNullException(nameof(watchedEpisodeIds));
        }
    }
}
