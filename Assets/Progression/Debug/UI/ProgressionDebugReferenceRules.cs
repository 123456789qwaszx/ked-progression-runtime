namespace Ked.Progression.Debugging.UI
{
    // ked-presentation-runtime/refactor/offline-local-save의 전환 규칙을
    // Progression Debug 화면에서 비교하기 위한 읽기 전용 명세.
    //
    // 실제 PresentationSession/Stage/Yarn 객체를 흉내 내는 상태가 아니다.
    // Target의 실제 실행 결과는 ProgressionDebugSnapshot으로 보고,
    // 이 클래스는 Reference에서 같은 사용자 동작이 어떤 경계를 거치는지만 설명한다.
    public static class ProgressionDebugReferenceRules
    {
        public const string Reference =
            "ked-presentation-runtime/refactor/offline-local-save @ df8ec2cf";

        public enum Transition
        {
            NewGame,
            Continue,
            ManualLoad,
            Stop,
            CompleteNode,
            EpisodeSkip,
            Rollback,
            BacklogJump,
        }

        public static string Describe(Transition transition)
        {
            switch (transition)
            {
                case Transition.NewGame:
                    return Report(
                        "NEW GAME",
                        "[MATCH] execution\n  Target: 현재 run Stop 후 새 run Start\n  Ref   : TransitionAsync = Stop -> change -> Launch",
                        "[MATCH] chapter / scene\n  Target: Chapter entry state + root Scene\n  Ref   : resume 미채택 시 StartChapter.CreateEntryState()",
                        "[MATCH] pending\n  기존 Scene pending은 취소되며 commit하지 않음",
                        "[PRESENTATION-ONLY] BeginScene\n  Variable checkpoint Capture\n  Yarn ChoiceHistory Clear\n  Stage Clear / Scope Start\n  PresentationSession flags Reset");

                case Transition.Continue:
                    return Report(
                        "CONTINUE",
                        "[MATCH] execution guard\n  Target/Ref 모두 이미 실행 중이면 새 시작 금지",
                        "[MATCH] committed state\n  저장된 Chapter/Scene root의 확정 상태에서 시작",
                        "[MATCH] progression load path\n  Ref   : SavedLoadPlan.Path를 첫 Scene에서 소비\n  Target: ScenePathStep restorePath를 첫 Scene에서 소비\n  Debug : ep-b1 -> ep-b2 고정 fixture로 실제 경로 전달",
                        "[PRESENTATION-ONLY] restore payload\n  Ref는 Yarn variables + Backlog + line target도 함께 복원");

                case Transition.ManualLoad:
                    return Report(
                        "MANUAL LOAD",
                        "[MATCH] execution\n  현재 run Stop 후 선택한 저장 상태로 새 run Start",
                        "[MATCH] pending\n  중단된 현재 Scene pending은 commit하지 않고 폐기",
                        "[MATCH] progression load path\n  Ref   : SavedLoadPlan.Path를 첫 Scene에서 소비\n  Target: 같은 ScenePathStep restorePath 계약 사용\n  Debug : ep-b1 -> ep-b2 고정 fixture를 전달",
                        "[PRESENTATION-ONLY] save boundary\n  slot/file/server/version, Yarn choice/line target은 Progression 밖의 책임");

                case Transition.Stop:
                    return Report(
                        "STOP / TITLE EXIT",
                        "[MATCH] execution\n  현재 run cancellation 후 idle",
                        "[MATCH] COMMIT FORBIDDEN\n  정상 SceneEnded/ChapterEnded가 아니므로 pending commit 없음",
                        "[PRESENTATION-ONLY] playback cleanup\n  EpisodeSkip Cancel / node Stop / line Abort\n  Rollback points + shot response Clear\n  PresentationScope End");

                case Transition.CompleteNode:
                    return Report(
                        "NORMAL NODE COMPLETE",
                        "[MATCH] playback\n  node 완료 뒤 정상 progression 경로를 계속 진행",
                        "[MATCH] commit boundary\n  node 완료 자체는 Scene commit이 아님\n  Scene/Chapter 경계를 실제로 넘을 때만 commit",
                        "[PRESENTATION-ONLY] Episode playback 종료\n  Reference는 one-shot EpisodeSkip 상태도 CompleteEpisode()로 종료");

                case Transition.EpisodeSkip:
                    return Report(
                        "EPISODE SKIP",
                        "[MATCH] progression cursor\n  Skip이 CurrentEpisodeId를 직접 바꾸지 않음",
                        "[MATCH] progression semantics\n  현재 node playback만 완료시키고 이후는 정상 진행과 동일",
                        "[PRESENTATION-ONLY] Reference\n  EpisodeSkipController는 playback 수명에 속하며 node 종료 시 CompleteEpisode()");

                case Transition.Rollback:
                    return Report(
                        "ROLLBACK",
                        "[MATCH] Scene\n  현재 Scene/EntryState 유지, root부터 playback 재시작",
                        "[MATCH] pending\n  rollback target 뒤의 pending/history 제거 후 기록 경로 재사용",
                        "[MATCH] commit\n  replay 자체는 Scene commit이 아님",
                        "[PRESENTATION-ONLY] replay prepare\n  Yarn variable checkpoint Restore\n  Stage Clear / Scope Start");

                case Transition.BacklogJump:
                    return Report(
                        "BACKLOG JUMP",
                        "[MATCH] current Scene\n  현재 Scene의 backlog는 Rollback과 같은 replay-to-target\n  Scene/EntryState 유지, target 이후 pending/history 제거",
                        "[OUTSIDE-PROGRESSION] previous Scene\n  완료된 과거 Scene은 replay가 아니라 Stop -> checkpoint 복원 -> 새 run/fork",
                        "[HARNESS GAP] previous Scene fork\n  Target Runtime의 Stop/Start primitive는 있으나 Debug Host는 cross-Scene Save fork를 모델링하지 않음",
                        "[PRESENTATION-ONLY] target selection / replay prepare\n  backlog line 해석, variable checkpoint, Stage/Scope 복원은 Runtime 밖의 책임");

                default:
                    return Report("UNKNOWN", "비교 규칙 없음");
            }
        }

        private static string Report(string title, params string[] rows)
        {
            return
                "Reference parity\n" +
                Reference + "\n\n" +
                title + "\n" +
                "--------------------------------\n" +
                string.Join("\n\n", rows);
        }
    }
}
