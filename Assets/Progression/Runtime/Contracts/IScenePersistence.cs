namespace Ked.Progression
{
    // Scene 경계에서 Host가 확정 저장을 수행하는 실행 계약.
    // Runtime은 순수 Progression 데이터만 전달한다.
    // Backlog, Yarn 선택 기록, 파일 형식과 Playthrough 정책은 Host 구현이 조합한다.
    // 메서드가 실패하면 현재 Scene 실행도 실패하며 다음 Scene으로 진행하지 않는다.
    public interface IScenePersistence
    {
        void EnterScene(
            string chapterId,
            string sceneId,
            ProgressionState entryState);

        void CommitScene(
            string chapterId,
            string sceneId,
            SceneCommitResult result,
            SceneRunOutcome outcome);
    }

    public sealed class NullScenePersistence : IScenePersistence
    {
        public static readonly NullScenePersistence Instance = new();

        private NullScenePersistence()
        {
        }

        public void EnterScene(
            string chapterId,
            string sceneId,
            ProgressionState entryState)
        {
        }

        public void CommitScene(
            string chapterId,
            string sceneId,
            SceneCommitResult result,
            SceneRunOutcome outcome)
        {
        }
    }
}
