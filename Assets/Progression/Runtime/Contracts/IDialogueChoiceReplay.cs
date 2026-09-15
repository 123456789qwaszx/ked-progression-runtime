using System.Collections.Generic;

namespace Ked.Progression
{
    public interface IDialogueChoiceReplay
    {
        void RestoreChoices(IReadOnlyList<DialogueChoiceRecord> choices);
    }
}
