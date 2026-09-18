using System.Collections.Generic;

namespace Ked.Progression
{
    // Progression이 실제로 어떤 lifecycle 경계를 통과했는지 관찰한다.
    //
    // 이 인터페이스는 Yarn/Stage/Save 같은 실제 작업을 수행하지 않는다.
    // 실제 구현 책임은 IScenePlayback / IScenePersistence 등의 contract가 맡고,
    // Reporter는 디버그 로그와 characterization test에서 호출 순서를 확인하는 용도다.
    public interface IProgressionReporter
    {
        void ReportChapterEntered(
            string chapterId,
            ProgressionState state);

        void ReportChapterExited(
            string chapterId,
            ProgressionState state);

        void ReportSceneEntered(
            string chapterId,
            string sceneId,
            ProgressionState entryState);

        void ReportSceneCommitted(
            string chapterId,
            string sceneId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state);

        void ReportSceneExited(
            string chapterId,
            string sceneId,
            ProgressionState committedState);

        void ReportEpisodeEntered(
            string chapterId,
            string sceneId,
            EpisodeNode episode);

        void ReportEpisodeExited(
            string chapterId,
            string sceneId,
            EpisodeNode episode);
    }
}
