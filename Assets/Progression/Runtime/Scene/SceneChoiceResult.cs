namespace Ked.Progression
{
    public enum SceneChoiceResultKind
    {
        Choice = 0,
        ChapterEnded = 1,
        ReplayRequested = 2,
    }

    public readonly struct SceneChoiceResult
    {
        public SceneChoiceResultKind Kind { get; }
        public SceneChoice Choice { get; }

        private SceneChoiceResult(
            SceneChoiceResultKind kind,
            SceneChoice choice)
        {
            Kind = kind;
            Choice = choice;
        }

        public static SceneChoiceResult FromChoice(SceneChoice choice) =>
            new(SceneChoiceResultKind.Choice, choice);

        public static SceneChoiceResult ChapterEnded() =>
            new(SceneChoiceResultKind.ChapterEnded, default);

        public static SceneChoiceResult ReplayRequested() =>
            new(SceneChoiceResultKind.ReplayRequested, default);
    }
}
