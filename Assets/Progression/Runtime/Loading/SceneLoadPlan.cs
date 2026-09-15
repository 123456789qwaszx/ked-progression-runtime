using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // Presentation 저장 모델을 Progression 실행 모델로 옮긴 선택 기록.
    // VNChoiceRecord와 필드 의미를 동일하게 유지하되 Presentation 타입에는 의존하지 않는다.
    public readonly struct DialogueChoiceRecord
    {
        public int AnchorHistoryIndex { get; }
        public int ChoiceSequence { get; }
        public int SelectedOptionIndex { get; }
        public string SelectedOptionLineId { get; }

        public DialogueChoiceRecord(
            int anchorHistoryIndex,
            int choiceSequence,
            int selectedOptionIndex,
            string selectedOptionLineId)
        {
            AnchorHistoryIndex = anchorHistoryIndex;
            ChoiceSequence = choiceSequence;
            SelectedOptionIndex = selectedOptionIndex;
            SelectedOptionLineId = selectedOptionLineId ?? string.Empty;
        }
    }

    public sealed class SceneLineTarget
    {
        public string NodeName { get; }
        public string LineId { get; }
        public int Occurrence { get; }

        public SceneLineTarget(string nodeName, string lineId, int occurrence)
        {
            NodeName = nodeName ?? string.Empty;
            LineId = lineId ?? string.Empty;
            Occurrence = occurrence;
        }
    }

    // 저장 계층의 SavedLoadPlan을 그대로 들고 오지 않는다.
    // Presentation adapter가 SavedLoadPlan -> SceneLoadPlan으로 변환한다.
    public sealed class SceneLoadPlan
    {
        public IReadOnlyList<ScenePathStep> Path { get; }
        public IReadOnlyList<DialogueChoiceRecord> DialogueChoices { get; }
        public SceneLineTarget Target { get; }

        public SceneLoadPlan(
            IReadOnlyList<ScenePathStep> path,
            IReadOnlyList<DialogueChoiceRecord> dialogueChoices,
            SceneLineTarget target)
        {
            Path = path ?? Array.Empty<ScenePathStep>();
            DialogueChoices = dialogueChoices ?? Array.Empty<DialogueChoiceRecord>();
            Target = target;
        }
    }
}
