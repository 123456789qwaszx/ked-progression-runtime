using System.Collections.Generic;

namespace Ked.Progression.Debugging.UI
{
    public enum ProgressionDebugParityStatus
    {
        Match,
        Diff,
        OutsideProgression,
        ProgressionOnly,
    }

    public enum ProgressionDebugParityEvidence
    {
        Verified,
        HarnessGap,
        CharacterizationNeeded,
    }

    public enum ProgressionDebugParityOwner
    {
        Progression,
        Presentation,
        Save,
        Host,
        UI,
    }

    public readonly struct ProgressionDebugComparisonRow
    {
        public string Topic { get; }
        public ProgressionDebugParityStatus Status { get; }
        public ProgressionDebugParityEvidence Evidence { get; }
        public ProgressionDebugParityOwner Owner { get; }
        public string Target { get; }
        public string Reference { get; }

        public ProgressionDebugComparisonRow(
            string topic,
            ProgressionDebugParityStatus status,
            ProgressionDebugParityEvidence evidence,
            ProgressionDebugParityOwner owner,
            string target,
            string reference)
        {
            Topic = topic;
            Status = status;
            Evidence = evidence;
            Owner = owner;
            Target = target;
            Reference = reference;
        }
    }

    public sealed class ProgressionDebugComparisonReport
    {
        public string Title { get; }
        public string ReferenceSource { get; }
        public IReadOnlyList<ProgressionDebugComparisonRow> Rows { get; }

        public ProgressionDebugComparisonReport(
            string title,
            string referenceSource,
            IReadOnlyList<ProgressionDebugComparisonRow> rows)
        {
            Title = title;
            ReferenceSource = referenceSource;
            Rows = rows;
        }
    }

    // ked-presentation-runtime/refactor/offline-local-save의 전환 규칙을
    // typed comparison report로 제공하는 Debug 전용 불변 명세.
    // Target의 실제 실행값은 ProgressionDebugSnapshot이 소유하고,
    // 이 클래스는 Reference와 비교할 의미/책임/검증 수준만 기술한다.
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
            BacklogPreviousScene,
        }

        public static ProgressionDebugComparisonReport CreateReport(
            Transition transition)
        {
            switch (transition)
            {
                case Transition.NewGame:
                    return Report(
                        "NEW GAME",
                        Match(
                            "execution",
                            "current run Stop → initial entry state로 새 run Start",
                            "TransitionAsync = Stop → change → Launch"),
                        Match(
                            "pending",
                            "버린 Scene pending을 commit하지 않음",
                            "기존 run 폐기 시 pending commit 없음"),
                        Outside(
                            "scene presentation reset",
                            ProgressionDebugParityOwner.Presentation,
                            "Progression Runtime 밖에서 준비",
                            "checkpoint capture / Stage clear / Scope start / session flags reset"));

                case Transition.Continue:
                    return Report(
                        "CONTINUE",
                        Match(
                            "execution guard",
                            "이미 실행 중이면 새 Continue 시작 금지",
                            "Reference도 running guard 적용"),
                        Match(
                            "committed checkpoint",
                            "저장된 Scene root state에서 시작",
                            "resume의 Scene root checkpoint에서 시작"),
                        Match(
                            "progression load path",
                            "ScenePathStep restorePath를 첫 Scene에서 1회 소비",
                            "SavedLoadPlan.Path를 첫 Scene에서 1회 소비"),
                        Outside(
                            "presentation restore payload",
                            ProgressionDebugParityOwner.Presentation,
                            "path validation 성공 후 Host replay state 시작",
                            "Yarn variables / Backlog / Yarn choices / line target 복원"));

                case Transition.ManualLoad:
                    return Report(
                        "MANUAL LOAD",
                        Match(
                            "execution",
                            "current run Stop → selected checkpoint로 새 run Start",
                            "TransitionAsync 후 선택 slot 기준 새 실행"),
                        Match(
                            "pending",
                            "중단된 Scene pending 폐기",
                            "기존 Scene pending commit 없음"),
                        Match(
                            "progression load path",
                            "ScenePathStep restorePath 계약 사용",
                            "SavedLoadPlan.Path 사용"),
                        Outside(
                            "save boundary",
                            ProgressionDebugParityOwner.Save,
                            "고정 fixture만 Host에서 준비",
                            "slot/file/server/version/Playthrough fork 처리"));

                case Transition.Stop:
                    return Report(
                        "STOP / TITLE EXIT",
                        MatchCharacterization(
                            "execution",
                            "run cancellation + playback/options stop",
                            "current playback/session stop"),
                        MatchCharacterization(
                            "commit forbidden",
                            "Stop 이후 Scene Commit/Exit 없음",
                            "외부 중단은 pending을 확정/보고하지 않는 계약"),
                        Outside(
                            "playback cleanup",
                            ProgressionDebugParityOwner.Presentation,
                            "Runtime은 playback.Stop / option.Cancel contract만 호출",
                            "EpisodeSkip cancel / line abort / rollback point / scope cleanup"));

                case Transition.CompleteNode:
                    return Report(
                        "NORMAL NODE COMPLETE",
                        Match(
                            "playback",
                            "node 완료 후 정상 progression 계속",
                            "node 완료 후 정상 SceneRunner 경로 계속"),
                        Match(
                            "commit boundary",
                            "node 완료 자체는 commit이 아님",
                            "Scene/Chapter 경계를 넘을 때만 commit"),
                        Outside(
                            "episode playback lifecycle",
                            ProgressionDebugParityOwner.Presentation,
                            "IScenePlayback 완료만 관측",
                            "EpisodeSkip one-shot 상태도 CompleteEpisode로 종료"));

                case Transition.EpisodeSkip:
                    return Report(
                        "EPISODE SKIP",
                        Match(
                            "progression cursor",
                            "Skip이 CurrentEpisodeId를 직접 변경하지 않음",
                            "Reference도 playback을 완료시킨 뒤 정상 progression 사용"),
                        Outside(
                            "skip controller",
                            ProgressionDebugParityOwner.Presentation,
                            "Debug Host가 현재 node gate만 완료",
                            "EpisodeSkipController가 playback lifetime 소유"));

                case Transition.Rollback:
                    return Report(
                        "ROLLBACK",
                        Match(
                            "Scene identity",
                            "현재 Scene / EntryState 유지",
                            "동일 Scene checkpoint 유지"),
                        Match(
                            "pending rewind",
                            "target 이후 choice/watched 제거 후 root replay",
                            "rollback target 이후 history 제거 후 root replay"),
                        Match(
                            "commit",
                            "replay 자체는 commit하지 않음",
                            "Reference도 replay branch와 commit branch 분리"),
                        Outside(
                            "presentation replay prepare",
                            ProgressionDebugParityOwner.Presentation,
                            "PrepareReplayAsync contract만 호출",
                            "variable checkpoint restore / Stage clear / Scope start"));

                case Transition.BacklogJump:
                    return Report(
                        "BACKLOG / CURRENT SCENE",
                        Match(
                            "progression primitive",
                            "Rollback과 같은 Replay(target)",
                            "현재 Scene backlog도 same-Scene replay"),
                        Match(
                            "Scene / pending",
                            "Scene 유지 + target 이후 history 제거",
                            "동일 Scene 유지 + target 이후 기록 제거"),
                        Outside(
                            "target selection",
                            ProgressionDebugParityOwner.UI,
                            "Debug에서는 history index fixture 사용",
                            "backlog line을 rollback anchor로 해석"));

                case Transition.BacklogPreviousScene:
                    return Report(
                        "BACKLOG / PREVIOUS SCENE",
                        Outside(
                            "fork orchestration",
                            ProgressionDebugParityOwner.Save,
                            "Host fixture가 historical checkpoint/path를 준비",
                            "SaveCoordinator가 SceneCheckpoint/records 선택 + 새 Playthrough 생성"),
                        Match(
                            "Runtime primitive",
                            "Stop → Start(ep-a1 checkpoint, ep-a1→ep-a2 restorePath)",
                            "current run Stop → historical Scene checkpoint로 Launch"),
                        Match(
                            "pending boundary",
                            "버린 current Scene pending commit 없음",
                            "fork 전 current Scene은 정상 완료가 아니므로 commit 없음"),
                        Outside(
                            "historical presentation payload",
                            ProgressionDebugParityOwner.Presentation,
                            "Progression path까지만 fixture로 전달",
                            "Backlog/Yarn choices/line target/Stage restore"));

                default:
                    return Report("UNKNOWN");
            }
        }

        private static ProgressionDebugComparisonReport Report(
            string title,
            params ProgressionDebugComparisonRow[] rows)
        {
            return new ProgressionDebugComparisonReport(
                title,
                Reference,
                rows);
        }

        private static ProgressionDebugComparisonRow Match(
            string topic,
            string target,
            string reference)
        {
            return new ProgressionDebugComparisonRow(
                topic,
                ProgressionDebugParityStatus.Match,
                ProgressionDebugParityEvidence.Verified,
                ProgressionDebugParityOwner.Progression,
                target,
                reference);
        }

        private static ProgressionDebugComparisonRow MatchCharacterization(
            string topic,
            string target,
            string reference)
        {
            return new ProgressionDebugComparisonRow(
                topic,
                ProgressionDebugParityStatus.Match,
                ProgressionDebugParityEvidence.CharacterizationNeeded,
                ProgressionDebugParityOwner.Progression,
                target,
                reference);
        }

        private static ProgressionDebugComparisonRow Outside(
            string topic,
            ProgressionDebugParityOwner owner,
            string target,
            string reference)
        {
            return new ProgressionDebugComparisonRow(
                topic,
                ProgressionDebugParityStatus.OutsideProgression,
                ProgressionDebugParityEvidence.Verified,
                owner,
                target,
                reference);
        }
    }
}
