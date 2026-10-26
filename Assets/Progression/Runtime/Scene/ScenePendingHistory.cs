using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    internal sealed class ScenePendingHistory
    {
        private sealed class ProgressionPick
        {
            public EpisodeOption Option;
            public string FromEpisodeId;
            public int SourceIndex;
            public int Anchor;
        }

        private sealed class WatchedEpisode
        {
            public string EpisodeId;
            public int Anchor;
        }

        private readonly List<ProgressionPick> _picks = new();
        private readonly List<WatchedEpisode> _watched = new();
        private readonly List<EpisodeOption> _foldBuffer = new();

        public int PathCursor { get; private set; }
        public bool HasRecordedChoice => PathCursor < _picks.Count;
        public int RecordedChoiceCount => _picks.Count;

        public void RestoreChoice(EpisodeOption option, string fromEpisodeId, int sourceIndex)
        {
            _picks.Add(new ProgressionPick
            {
                Option = option,
                FromEpisodeId = fromEpisodeId,
                SourceIndex = sourceIndex,
                Anchor = -1,
            });
        }

        public SceneChoice TakeRecordedChoice(int anchor)
        {
            ProgressionPick pick = _picks[PathCursor++];
            pick.Anchor = anchor;

            return new SceneChoice(
                pick.Option,
                pick.FromEpisodeId,
                pick.SourceIndex,
                SceneChoiceSource.Recorded);
        }

        public void RecordChoice(SceneChoice choice, int anchor)
        {
            if (choice.Source == SceneChoiceSource.Recorded)
                throw new InvalidOperationException("이미 기록된 선택을 pending history에 다시 추가할 수 없다.");

            if (PathCursor != _picks.Count)
                throw new InvalidOperationException("소비되지 않은 이전 진행 선택 기록이 남아 있다.");

            _picks.Add(new ProgressionPick
            {
                Option = choice.Option,
                FromEpisodeId = choice.FromEpisodeId,
                SourceIndex = choice.SourceIndex,
                Anchor = anchor,
            });

            PathCursor = _picks.Count;
        }

        public void DiscardUnconsumedChoices()
        {
            if (!HasRecordedChoice)
                return;

            _picks.RemoveRange(PathCursor, _picks.Count - PathCursor);
        }

        public void ResetRecordedChoiceCursor()
        {
            PathCursor = 0;
        }

        public void TruncateAfter(int historyIndex)
        {
            for (int i = _picks.Count - 1; i >= 0; i--)
            {
                if (_picks[i].Anchor > historyIndex)
                    _picks.RemoveAt(i);
            }

            for (int i = _watched.Count - 1; i >= 0; i--)
            {
                if (_watched[i].Anchor > historyIndex)
                    _watched.RemoveAt(i);
            }

            if (PathCursor > _picks.Count)
                PathCursor = _picks.Count;
        }

        public void ClearChoices()
        {
            _picks.Clear();
            PathCursor = 0;
        }

        public void NoteWatched(EpisodeNode episode, int anchor)
        {
            if (string.IsNullOrEmpty(episode.EventKey))
                return;

            for (int i = 0; i < _watched.Count; i++)
            {
                if (string.Equals(_watched[i].EpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                    return;
            }

            _watched.Add(new WatchedEpisode
            {
                EpisodeId = episode.EpisodeId,
                Anchor = anchor,
            });
        }

        public IReadOnlyList<EpisodeOption> AppliedOptions()
        {
            _foldBuffer.Clear();

            for (int i = 0; i < PathCursor; i++)
                _foldBuffer.Add(_picks[i].Option);

            return _foldBuffer;
        }

        public ChapterState ApplyTo(
            ChapterDefinition chapter,
            ChapterState entryState)
        {
            return entryState.ApplyChoices(chapter, AppliedOptions());
        }

        public List<CommittedChoice> CreateCommittedChoices()
        {
            var result = new List<CommittedChoice>(PathCursor);

            for (int i = 0; i < PathCursor; i++)
            {
                ProgressionPick pick = _picks[i];
                result.Add(new CommittedChoice(pick.FromEpisodeId, pick.SourceIndex));
            }

            return result;
        }

        public List<string> CreateWatchedEpisodeIds()
        {
            var result = new List<string>(_watched.Count);

            for (int i = 0; i < _watched.Count; i++)
                result.Add(_watched[i].EpisodeId);

            return result;
        }

        public IReadOnlyList<CommittedChoice> CreatePendingPath()
        {
            var result = new List<CommittedChoice>(PathCursor);

            for (int i = 0; i < PathCursor; i++)
            {
                ProgressionPick pick = _picks[i];
                result.Add(new CommittedChoice(pick.FromEpisodeId, pick.SourceIndex));
            }

            return result;
        }
    }
}
