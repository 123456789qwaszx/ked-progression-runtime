using System.Collections.Generic;

namespace Ked.Progression
{
    public sealed class SceneTransaction
    {
        public ChapterProgression Chapter { get; }
        public ProgressionState EntryState { get; }

        public string RootEpisodeId { get; }
        public string CurrentEpisodeId { get; private set; }

        public SavedLoadPlan LoadPlan { get; }

        public SceneRunPhase Phase { get; private set; } = SceneRunPhase.None;
        public bool ReplayPending { get; private set; }

        public EpisodeNode RootEpisode => GetEpisode(RootEpisodeId);
        public EpisodeNode CurrentEpisode => GetEpisode(CurrentEpisodeId);

        public IReadOnlyList<CommittedChoice> PendingPath =>
            History.CreatePendingPath();

        internal ScenePendingHistory History { get; } = new();

        public SceneTransaction(
            ChapterProgression chapter,
            ProgressionState entryState,
            SavedLoadPlan loadPlan = null)
        {
            Chapter = chapter;
            EntryState = entryState;

            RootEpisodeId = entryState.CurrentEpisodeId;
            CurrentEpisodeId = RootEpisodeId;

            LoadPlan = loadPlan;
        }

        internal void SetPhase(SceneRunPhase phase)
        {
            Phase = phase;
        }

        internal void MoveTo(string episodeId)
        {
            CurrentEpisodeId = episodeId;
        }

        internal bool RequestReplay()
        {
            if (ReplayPending)
                return false;

            ReplayPending = true;
            return true;
        }

        internal void RestartFromRoot()
        {
            CurrentEpisodeId = RootEpisodeId;
            ReplayPending = false;
        }

        private EpisodeNode GetEpisode(string episodeId)
        {
            Chapter.TryGetNode(episodeId, out EpisodeNode episode);
            return episode;
        }
    }
}
