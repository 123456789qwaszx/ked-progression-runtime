using Ked.Progression.Save;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 장면 경계에서 진행 저장을 부른다(IScenePersistence).
    /// 호스트 순번과 호스트 상태는 여기서 대사 기록으로부터 싣는다 — 진행 저장은 그 뜻을 모른다.
    /// </summary>
    public sealed class SampleSaveBridge : IScenePersistence
    {
        private readonly SaveCoordinator _save;
        private readonly SampleLineLog _lines;

        public SampleSaveBridge(SaveCoordinator save, SampleLineLog lines)
        {
            _save = save;
            _lines = lines;
        }

        // 실행기가 대사 기록에 장면 경계를 먼저 표시한 뒤에 부른다.
        public void EnterScene(string chapterId, ChapterState entryState) =>
            _save.EnterScene(chapterId, entryState, hostSerialStart: _lines.SceneStart);

        public void CommitScene(string chapterId, SceneCommitResult result, SceneRunOutcome outcome) =>
            _save.CommitScene(
                chapterId,
                result,
                sceneHostState: SampleHostState.Scene,
                playthroughHostState: SampleHostState.Playthrough(_lines.Entries),
                hostSerialEnd: _lines.NextSerial,
                outcome);
    }
}
