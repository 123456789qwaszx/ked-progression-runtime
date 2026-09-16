using System;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class SceneReplayLifecycleTests
    {
        [Test]
        public void Rewind_keeps_scene_open_and_removes_future_pending_choices()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            ChapterAdvance first = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(first.Options[0], SceneChoiceSource.User, rollbackAnchor: 10);

            ChapterAdvance second = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(second.Options[0], SceneChoiceSource.User, rollbackAnchor: 20);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(2));

            scene.RewindAfter(10);

            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(1));
            Assert.That(scene.IsCommitted, Is.False);
        }

        [Test]
        public void RestartReplay_moves_cursor_to_root_without_committing_scene()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            ChapterAdvance first = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(first.Options[0], SceneChoiceSource.User, rollbackAnchor: 10);

            scene.RestartReplay();

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.HasRecordedChoice, Is.True);
            Assert.That(scene.IsCommitted, Is.False);
        }

        [Test]
        public void Replay_consumes_recorded_choices_again_from_scene_root()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            ChapterAdvance first = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(first.Options[0], SceneChoiceSource.User, rollbackAnchor: 10);

            ChapterAdvance second = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(second.Options[0], SceneChoiceSource.User, rollbackAnchor: 20);

            scene.RestartReplay();

            SceneChoice replayedFirst = scene.TakeRecordedChoice(rollbackAnchor: 10);
            SceneChoice replayedSecond = scene.TakeRecordedChoice(rollbackAnchor: 20);

            Assert.That(replayedFirst.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(replayedFirst.FromEpisodeId, Is.EqualTo("a"));
            Assert.That(replayedSecond.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(replayedSecond.FromEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.HasRecordedChoice, Is.False);
            Assert.That(scene.IsCommitted, Is.False);
        }

        [Test]
        public void Restored_path_can_be_consumed_from_scene_root()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            EpisodeNode a = chapter.StartNode;
            EpisodeOption aToB = a.NextOptions[0];
            EpisodeNode b = chapter.Nodes[1];
            EpisodeOption bToC = b.NextOptions[0];

            scene.RestoreChoice(aToB, "a", 0);
            scene.RestoreChoice(bToC, "b", 0);

            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(2));
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));

            scene.TakeRecordedChoice(rollbackAnchor: 10);
            scene.TakeRecordedChoice(rollbackAnchor: 20);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.HasRecordedChoice, Is.False);
            Assert.That(scene.IsCommitted, Is.False);
        }

        [Test]
        public void RestorePath_ReplaysRecordedChoices()
        {
            ChapterProgression chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());
            var path = new[]
            {
                new ScenePathStep("a", 0),
                new ScenePathStep("b", 0),
            };

            bool restored = scene.TryRestorePath(path);

            Assert.That(restored, Is.True);
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(2));

            SceneChoice first = scene.TakeRecordedChoice(10);
            SceneChoice second = scene.TakeRecordedChoice(20);

            Assert.That(first.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(first.FromEpisodeId, Is.EqualTo("a"));
            Assert.That(second.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(second.FromEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.HasRecordedChoice, Is.False);
        }

        [Test]
        public void InvalidRestorePath_FallsBackToRoot()
        {
            ChapterProgression chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());
            var path = new[]
            {
                new ScenePathStep("a", 0),
                new ScenePathStep("wrong", 0),
            };

            bool restored = scene.TryRestorePath(path);

            Assert.That(restored, Is.False);
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.HasRecordedChoice, Is.False);
        }

        [Test]
        public void InvalidRestorePath_ClearsEntireRecordedPath()
        {
            ChapterProgression chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());
            var path = new[]
            {
                new ScenePathStep("a", 0),
                new ScenePathStep("b", 99),
            };

            bool restored = scene.TryRestorePath(path);

            Assert.That(restored, Is.False);
            Assert.That(scene.RecordedChoiceCount, Is.Zero);
            Assert.That(scene.HasRecordedChoice, Is.False);
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo(scene.RootEpisodeId));
        }

        [Test]
        public void RestorePath_DoesNotCommitScene()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            bool restored = scene.TryRestorePath(
                new[] { new ScenePathStep("a", 0) });

            Assert.That(restored, Is.True);
            Assert.That(scene.IsCommitted, Is.False);
            Assert.That(scene.EntryState, Is.SameAs(entry));
            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("a"));
        }

        private static ChapterProgression CreateChapter()
        {
            EpisodeOption aToB = EpisodeOption.Choice("A to B", "b");
            EpisodeOption bToC = EpisodeOption.Choice("B to C", "c");

            var a = new EpisodeNode(
                "a",
                "A",
                "dialogue_a",
                new[] { aToB },
                eventKey: "event_a",
                sceneId: "scene-a");

            var b = new EpisodeNode(
                "b",
                "B",
                "dialogue_b",
                new[] { bToC },
                eventKey: "event_b",
                sceneId: "scene-a");

            var c = new EpisodeNode(
                "c",
                "C",
                "dialogue_c",
                Array.Empty<EpisodeOption>(),
                eventKey: "event_c",
                sceneId: "scene-a");

            return new ChapterProgression(
                "chapter",
                "Chapter",
                "a",
                Array.Empty<StatDefinition>(),
                new[] { a, b, c });
        }
    }
}
