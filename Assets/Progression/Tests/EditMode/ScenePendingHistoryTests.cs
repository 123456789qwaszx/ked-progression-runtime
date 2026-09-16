using System;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class ScenePendingHistoryTests
    {
        [Test]
        public void RestartReplay_KeepsChoicesAndResetsCursor()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            EpisodeOption option = chapter.StartNode.NextOptions[0];

            var history = new ScenePendingHistory();
            history.RestoreChoice(option, "ep-1", 0);
            history.TakeRecordedChoice(10);

            Assert.That(history.PathCursor, Is.EqualTo(1));

            history.RestartReplay();

            Assert.That(history.PathCursor, Is.EqualTo(0));
            Assert.That(history.RecordedChoiceCount, Is.EqualTo(1));
            Assert.That(history.HasRecordedChoice, Is.True);
        }

        [Test]
        public void RewindAfter_RemovesChoicesPastAnchor()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            EpisodeOption option = chapter.StartNode.NextOptions[0];

            var history = new ScenePendingHistory();
            history.RecordChoice(
                new SceneChoice(option, "ep-1", 0, SceneChoiceSource.User),
                20);

            history.TruncateAfter(10);

            Assert.That(history.RecordedChoiceCount, Is.EqualTo(0));
            Assert.That(history.PathCursor, Is.EqualTo(0));
        }
    }
}
