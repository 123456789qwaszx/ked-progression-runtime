namespace Ked.Progression
{
    // Scene root부터 과거 progression 선택을 다시 따라가기 위한
    // 저장 가능한 최소 Progression 좌표.
    public readonly struct ScenePathStep
    {
        public string FromEpisodeId { get; }
        public int OptionIndex { get; }

        public ScenePathStep(string fromEpisodeId, int optionIndex)
        {
            FromEpisodeId = fromEpisodeId;
            OptionIndex = optionIndex;
        }
    }
}
