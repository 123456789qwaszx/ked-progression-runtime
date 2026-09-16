using System;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class SceneProgressionTests
    {
        [Test]
        public void Rewind_removes_future_pending_choices()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            AdvanceFirstOption(scene, rollbackAnchor: 10);
            AdvanceFirstOption(scene, rollbackAnchor: 20);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(2));

            scene.RewindAfter(10);

            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(1));
        }

        [Test]
        public void Rewind_removes_future_watched_events()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            scene.NoteCurrentEpisodeWatched(10);
            AdvanceFirstOption(scene, rollbackAnchor: 10);

            scene.NoteCurrentEpisodeWatched(20);
            AdvanceFirstOption(scene, rollbackAnchor: 20);

            scene.RewindAfter(10);

            SceneCommitResult commit = scene.CreateCommitResult();

            Assert.That(commit.WatchedEpisodeIds, Is.EqualTo(new[] { "a" }));
            Assert.That(commit.State.CurrentEpisodeId, Is.EqualTo("b"));
        }

        [Test]
        public void RestartReplay_resets_history_cursor_and_episode_cursor_to_root()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            AdvanceFirstOption(scene, rollbackAnchor: 10);

            scene.RestartReplay();

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.HasRecordedChoice, Is.True);
        }

        [Test]
        public void Recorded_choices_are_consumed_from_root_and_runtime_moves_cursor_after_each_choice()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            AdvanceFirstOption(scene, rollbackAnchor: 10);
            AdvanceFirstOption(scene, rollbackAnchor: 20);

            scene.RestartReplay();

            SceneChoice first = scene.TakeRecordedChoice(10);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));

            scene.MoveTo(first.Option.TargetEpisodeId);

            SceneChoice second = scene.TakeRecordedChoice(20);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("b"));

            scene.MoveTo(second.Option.TargetEpisodeId);

            Assert.That(first.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(first.FromEpisodeId, Is.EqualTo("a"));

            Assert.That(second.Source, Is.EqualTo(SceneChoiceSource.Recorded));
            Assert.That(second.FromEpisodeId, Is.EqualTo("b"));

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.HasRecordedChoice, Is.False);
        }

        [Test]
        public void RestorePath_replays_recorded_choices_from_scene_root()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            bool restored = scene.TryRestorePath(
                new[]
                {
                    new ScenePathStep("a", 0),
                    new ScenePathStep("b", 0),
                });

            Assert.That(restored, Is.True);
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.RecordedChoiceCount, Is.EqualTo(2));

            SceneChoice first = scene.TakeRecordedChoice(10);
            scene.MoveTo(first.Option.TargetEpisodeId);

            SceneChoice second = scene.TakeRecordedChoice(20);
            scene.MoveTo(second.Option.TargetEpisodeId);

            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(scene.HasRecordedChoice, Is.False);
        }

        [Test]
        public void InvalidRestorePath_clears_entire_recorded_path_and_falls_back_to_root()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            bool restored = scene.TryRestorePath(
                new[]
                {
                    new ScenePathStep("a", 0),
                    new ScenePathStep("wrong", 0),
                });

            Assert.That(restored, Is.False);
            Assert.That(scene.RecordedChoiceCount, Is.Zero);
            Assert.That(scene.HasRecordedChoice, Is.False);
            Assert.That(scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("a"));
        }

        [Test]
        public void Commit_projects_working_state_choices_and_watched_events()
        {
            ChapterDefinition chapter = CreateChapter();
            var scene = new SceneProgression(chapter, chapter.CreateEntryState());

            scene.NoteCurrentEpisodeWatched(10);
            AdvanceFirstOption(scene, rollbackAnchor: 10);
            scene.NoteCurrentEpisodeWatched(20);

            SceneCommitResult commit = scene.CreateCommitResult();

            Assert.That(commit.State.CurrentEpisodeId, Is.EqualTo("b"));

            Assert.That(commit.Choices.Count, Is.EqualTo(1));
            Assert.That(commit.Choices[0].FromEpisodeId, Is.EqualTo("a"));
            Assert.That(commit.Choices[0].OptionIndex, Is.EqualTo(0));

            Assert.That(commit.WatchedEpisodeIds, Is.EqualTo(new[] { "a", "b" }));
        }

        private static void AdvanceFirstOption(
            SceneProgression scene,
            int rollbackAnchor)
        {
            ChapterAdvance advance =
                ChapterTransition.Resolve(
                    scene.Definition,
                    scene.WorkingState);

            scene.Advance(
                advance.Options[0],
                SceneChoiceSource.User,
                rollbackAnchor);
        }

        private static ChapterDefinition CreateChapter()
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

            return new ChapterDefinition(
                "chapter",
                "Chapter",
                "a",
                Array.Empty<StatDefinition>(),
                new[] { a, b, c });
        }
    }
}