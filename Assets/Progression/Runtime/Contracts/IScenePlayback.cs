using System.Threading.Tasks;

namespace Ked.Progression
{
    public interface IScenePlayback
    {
        Task BeginSceneAsync();
        Task PlayNodeAsync(string nodeName);
        Task PrepareReplayAsync();
        Task StopAsync();
    }
}
