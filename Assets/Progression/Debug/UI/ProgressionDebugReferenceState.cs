namespace Ked.Progression.Debugging.UI
{
    // Reference Presentation runtime의 예상 상태를 보여주기 위한 Debug 전용 mirror.
    // Target 런타임의 실제 상태가 아니며 ked-presentation-runtime의 객체를 모사하거나 의존하지 않는다.
    public sealed class ProgressionDebugReferenceState
    {
        public string Playback { get; private set; } = "Idle";
        public string Scope { get; private set; } = "Ended";
        public string Stage { get; private set; } = "Not started";
        public string VariableCheckpoint { get; private set; } = "None";
        public string ChoiceHistory { get; private set; } = "Not started";
        public string RollbackPoints { get; private set; } = "Not started";
        public string ShotResponses { get; private set; } = "Not started";
        public string SessionFlags { get; private set; } = "Not started";
        public string EpisodeSkip { get; private set; } = "Idle";

        public void BeginScene()
        {
            Playback = "Running";
            Scope = "Running (new CommandRunScope)";
            Stage = "Cleared";
            VariableCheckpoint = "Captured";
            ChoiceHistory = "Cleared";
            RollbackPoints = "Cleared by pre-stop";
            ShotResponses = "Cleared by pre-stop";
            SessionFlags = "Reset on Scope.Start";
            EpisodeSkip = "Idle / previous skip cancelled";
        }

        public void PrepareReplay()
        {
            Playback = "Running (replay from Scene root)";
            Scope = "Running (new CommandRunScope)";
            Stage = "Cleared";
            VariableCheckpoint = "Restored";
            ChoiceHistory = "Kept";
            RollbackPoints = "Cleared by stop";
            ShotResponses = "Cleared by stop";
            SessionFlags = "Reset on Scope.Start";
            EpisodeSkip = "Cancelled before replay";
        }

        public void Stop()
        {
            Playback = "Stopped";
            Scope = "Ended";
            Stage = "Kept (Stop does not Clear)";
            VariableCheckpoint = "Kept (Stop does not Clear)";
            ChoiceHistory = "Kept (Stop does not Clear)";
            RollbackPoints = "Cleared";
            ShotResponses = "Cleared";
            SessionFlags = "Not reset on Scope.End";
            EpisodeSkip = "Cancelled";
        }

        public void CompleteEpisode()
        {
            EpisodeSkip = "CompleteEpisode()";
        }

        public string Describe()
        {
            return
                "Reference / expected presentation state\n" +
                "--------------------------------\n" +
                $"Playback           : {Playback}\n" +
                $"PresentationScope  : {Scope}\n" +
                $"Stage              : {Stage}\n" +
                $"VariableCheckpoint : {VariableCheckpoint}\n" +
                $"ChoiceHistory      : {ChoiceHistory}\n" +
                $"RollbackPoints     : {RollbackPoints}\n" +
                $"ShotResponses      : {ShotResponses}\n" +
                $"SessionFlags       : {SessionFlags}\n" +
                $"EpisodeSkip        : {EpisodeSkip}";
        }
    }
}
