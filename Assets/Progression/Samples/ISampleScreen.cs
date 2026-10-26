using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 샘플 장면 실행기가 화면에 바라는 것. 대사 한 줄을 보이고, 장면 경계를 보여 준다.
    ///
    /// 무엇으로 재생할지는 호스트가 정한다. 이 샘플은 텍스트 줄로,
    /// ked-presentation-runtime은 같은 DialogueEntryId를 Yarn 노드로 재생한다.
    /// </summary>
    public interface ISampleScreen
    {
        void ShowSceneEntered(SceneProgress progress);

        /// <summary>대사 한 줄을 보인다. 기다리지 않는다.</summary>
        void ShowLine(EpisodeNode episode, string line);

        /// <summary>플레이어가 [다음]을 누를 때까지 기다린다.</summary>
        Task WaitNextAsync(CancellationToken cancellationToken);

        void ShowSceneCommitted(SceneCommitResult commit, SceneRunOutcome outcome);

        /// <summary>기다리는 중인 [다음]을 끝낸다.</summary>
        void Cancel();
    }
}
