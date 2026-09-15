using Ked.Progression;

internal enum SceneChoiceSource
{
    Recorded = 0,
    AutoAdvance = 1,
    User = 2,
}

internal readonly struct SceneChoice
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