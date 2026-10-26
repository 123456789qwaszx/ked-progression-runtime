using System.Collections.Generic;

namespace Ked.Progression.Save
{
    // 활성 회차를 이어할 때 호스트가 받는 재개 지점.
    public sealed class ResumePoint
    {
        public string ChapterId { get; }
        public string EpisodeId { get; }
        public IReadOnlyDictionary<string, int> Stats { get; }

        // 회차 전체의 호스트 상태. 호스트가 되읽는다.
        public string HostState { get; }

        // 첫 장면에서 저장 지점까지 달리는 계획. 없으면 장면 루트에서 시작한다.
        public SavedLoadPlan LoadPlan { get; }

        public bool ChapterCompleted { get; }

        public ResumePoint(
            string chapterId,
            string episodeId,
            IReadOnlyDictionary<string, int> stats,
            string hostState,
            SavedLoadPlan loadPlan,
            bool chapterCompleted)
        {
            ChapterId = chapterId;
            EpisodeId = episodeId;
            Stats = stats;
            HostState = hostState ?? "";
            LoadPlan = loadPlan;
            ChapterCompleted = chapterCompleted;
        }
    }
}
