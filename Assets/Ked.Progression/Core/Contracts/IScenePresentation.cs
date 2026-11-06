using System.Threading.Tasks;

namespace Ked.Progression
{
    public interface IScenePresentation
    {
        void BeginScene();
        Task PlayEpisodeAsync(string nodeName);
        void PrepareReplay();
        Task StopAsync();
    }
}
