using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression
{
    public interface ISceneRunner
    {
        Task<SceneRunResult> RunAsync(
            SceneRunSession context,
            CancellationToken cancellationToken);

        Task RequestReplayAsync(SceneRunSession scene);

        Task StopAsync();
    }
}
