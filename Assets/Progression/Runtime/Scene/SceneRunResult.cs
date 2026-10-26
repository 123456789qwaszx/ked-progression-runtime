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
        public ChapterState ExitState { get; }

        public SceneRunResult(SceneRunOutcome outcome, ChapterState state)
        {
            Outcome = outcome;
            ExitState = state;
        }
    }
}
