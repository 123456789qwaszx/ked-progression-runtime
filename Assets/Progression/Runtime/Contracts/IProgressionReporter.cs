using System.Collections.Generic;

namespace Ked.Progression
{
    public interface IProgressionReporter
    {
        void ReportSceneEntered(
            string chapterId,
            ProgressionState entryState);

        void ReportSceneCommitted(
            string chapterId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state);
    }
}
