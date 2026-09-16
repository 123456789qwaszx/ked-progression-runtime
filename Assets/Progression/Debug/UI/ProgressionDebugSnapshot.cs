namespace Ked.Progression.Debugging
{
    public readonly struct ProgressionDebugSnapshot
    {
        public bool IsRunning { get; }
        public string ChapterId { get; }
        public string SceneId { get; }
        public string EpisodeId { get; }
        public string NodeName { get; }
        public int HistoryIndex { get; }
        public int PendingCount { get; }
        public bool IsSeeking { get; }
        public string SavedEpisodeId { get; }

        public ProgressionDebugSnapshot(
            bool isRunning,
            string chapterId,
            string sceneId,
            string episodeId,
            string nodeName,
            int historyIndex,
            int pendingCount,
            bool isSeeking,
            string savedEpisodeId)
        {
            IsRunning = isRunning;
            ChapterId = chapterId;
            SceneId = sceneId;
            EpisodeId = episodeId;
            NodeName = nodeName;
            HistoryIndex = historyIndex;
            PendingCount = pendingCount;
            IsSeeking = isSeeking;
            SavedEpisodeId = savedEpisodeId;
        }
    }
}
