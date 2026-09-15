namespace Ked.Progression
{
    public enum SceneRunOutcome
    {
        SceneEnded = 0,
        ChapterEnded = 1,
    }

    public readonly struct SceneRunResult
    {
        public SceneRunOutcome Outcome { get; }
        public ProgressionState State { get; }

        public SceneRunResult(SceneRunOutcome outcome, ProgressionState state)
        {
            Outcome = outcome;
            State = state;
        }
    }
}
