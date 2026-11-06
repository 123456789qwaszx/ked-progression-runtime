namespace Ked.Progression
{
    public enum SceneChoiceSource
    {
        Recorded = 0,
        AutoAdvance = 1,
        User = 2,
    }

    // Scene 안에서 실제로 소비된 진행 선택.
    // Replay 시에는 저장된 진행 경로를 Recorded source로 다시 소비한다.
    public readonly struct SceneChoice
    {
        public EpisodeOption Option { get; }
        public string FromEpisodeId { get; }
        public int SourceIndex { get; }
        public SceneChoiceSource Source { get; }

        public SceneChoice(
            EpisodeOption option,
            string fromEpisodeId,
            int sourceIndex,
            SceneChoiceSource source)
        {
            Option = option;
            FromEpisodeId = fromEpisodeId;
            SourceIndex = sourceIndex;
            Source = source;
        }
    }
}
