namespace Ked.Progression
{
    // Load / rollback replay에 필요한 Presentation-side 상태 경계.
    //
    // Runtime은 Yarn choice 기록이나 line target을 소유하지 않는다.
    // Host가 SavedLoadPlan의 presentation 데이터를 미리 준비해 두고,
    // progression path 검증이 성공한 뒤 BeginLoadReplay()에서 실제 복원을 시작한다.
    public interface ISceneReplayState
    {
        bool IsSeekingActive { get; }

        void BeginLoadReplay();

        void ClearSeek();
    }
}
