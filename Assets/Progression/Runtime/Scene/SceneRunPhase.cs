namespace Ked.Progression
{
    public enum SceneRunPhase
    {
        None = 0,
        SceneEntering = 1,
        SceneEntered = 2,
        EntryReported = 3,
        RestorePathApplied = 4,
        EpisodePlaying = 5,
        EpisodeCompleted = 6,
        ChoiceResolving = 7,
        ChoiceResolved = 8,
        ViaPlaying = 9,
        TargetMoved = 10,
        Replaying = 11,
        SceneCommitting = 12,
        SceneCommitted = 13,
        Cancelled = 14,
        Faulted = 15,
    }
}
