using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 샘플 장면 실행기가 화면에 바라는 것. 에피소드를 "재생"하고 장면 경계를 보여 준다.
    ///
    /// 무엇으로 재생할지는 호스트가 정한다. 이 샘플은 텍스트 줄로,
    /// ked-presentation-runtime은 같은 DialogueEntryId를 Yarn 노드로 재생한다.
    /// </summary>
    public interface ISampleScreen
    {
        /// <summary>에피소드 하나를 끝까지 보여 준다. 다 보면 끝난다.</summary>
        Task PlayEpisodeAsync(EpisodeNode episode, CancellationToken cancellationToken);

        void ShowSceneEntered(SceneProgress progress);

        void ShowSceneCommitted(SceneCommitResult commit, SceneRunOutcome outcome);

        /// <summary>기다리는 중인 재생을 끝낸다.</summary>
        void Cancel();
    }
}
