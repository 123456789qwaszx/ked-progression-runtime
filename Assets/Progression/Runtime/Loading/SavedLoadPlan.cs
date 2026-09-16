using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    public sealed class SavedChoice
    {
        public string FromEpisodeId { get; }
        public int OptionIndex { get; }

        public SavedChoice(string fromEpisodeId, int optionIndex)
        {
            FromEpisodeId = fromEpisodeId ?? string.Empty;
            OptionIndex = optionIndex;
        }
    }

    public sealed class SavedLineTarget
    {
        public string NodeName { get; }
        public string LineId { get; }
        public int Occurrence { get; }

        public SavedLineTarget(string nodeName, string lineId, int occurrence)
        {
            NodeName = nodeName ?? string.Empty;
            LineId = lineId ?? string.Empty;
            Occurrence = occurrence;
        }
    }

    public sealed class SavedLoadPlan
    {
        public IReadOnlyList<SavedChoice> Path { get; }
        public IReadOnlyList<int> DialogueChoices { get; }
        public SavedLineTarget Target { get; }

        public SavedLoadPlan(
            IReadOnlyList<SavedChoice> path,
            IReadOnlyList<int> dialogueChoices,
            SavedLineTarget target)
        {
            Path = path ?? Array.Empty<SavedChoice>();
            DialogueChoices = dialogueChoices ?? Array.Empty<int>();
            Target = target;
        }
    }
}
