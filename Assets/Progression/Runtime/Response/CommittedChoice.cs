namespace Ked.Progression
{
    // 장면 안에서 확정된 선택 하나.
    public readonly struct CommittedChoice
    {
        public string FromEpisodeId { get; }
        public int OptionIndex { get; }

        public CommittedChoice(string fromEpisodeId, int optionIndex)
        {
            FromEpisodeId = fromEpisodeId;
            OptionIndex = optionIndex;
        }
    }
}
