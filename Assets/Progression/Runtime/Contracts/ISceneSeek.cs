namespace Ked.Progression
{
    public interface ISceneSeek
    {
        bool IsSeekingActive { get; }

        void BeginLoadSeek(
            string nodeName,
            string lineId,
            int occurrence);

        void ClearSeek();
    }
}
