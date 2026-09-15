namespace Ked.Progression
{
    internal enum SceneChoiceResolutionKind
    {
        Choice = 0,
        ChapterEnded = 1,
        ReplayRequested = 2,
    }

    internal readonly struct SceneChoiceResolution
    {
        public SceneChoiceResolutionKind Kind { get; }
        public SceneChoice Choice { get; }

        private SceneChoiceResolution(
            SceneChoiceResolutionKind kind,
            SceneChoice choice)
        {
            Kind = kind;
            Choice = choice;
        }

        public static SceneChoiceResolution FromChoice(SceneChoice choice) =>
            new(SceneChoiceResolutionKind.Choice, choice);

        public static SceneChoiceResolution ChapterEnded() =>
            new(SceneChoiceResolutionKind.ChapterEnded, default);

        public static SceneChoiceResolution ReplayRequested() =>
            new(SceneChoiceResolutionKind.ReplayRequested, default);
    }
}
