using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class ChapterSessionLifecycleTests
    {
        [Test]
        public async Task Chapter_runs_boundaries_in_chapter_scene_episode_order()
        {
            ChapterProgression chapter = CreateChapter();
            var recorder = new BoundaryRecorder();
            var boundaries = new ProgressionBoundaries(recorder, recorder, recorder);
            var session = new ChapterSession(chapter, boundaries);

            await session.EnterAsync();

            ChapterAdvance first = await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceAsync(first.Options[0], SceneChoiceSource.User);

            ChapterAdvance second = await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceAsync(second.Options[0], SceneChoiceSource.User);

            ChapterAdvance last = await session.CompleteCurrentEpisodeAsync();

            Assert.That(last.Kind, Is.EqualTo(ChapterAdvanceKind.ChapterEnded));
            Assert.That(session.IsCompleted, Is.True);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Chapter.Enter:chapter",
                    "Scene.Enter:scene-a",
                    "Episode.Enter:a",
                    "Episode.Exit:a",
                    "Episode.Enter:b",
                    "Episode.Exit:b",
                    "Scene.Exit:scene-a",
                    "Scene.Enter:scene-b",
                    "Episode.Enter:c",
                    "Episode.Exit:c",
                    "Scene.Exit:scene-b",
                    "Chapter.Exit:chapter",
                },
                recorder.Events);
        }

        [Test]
        public async Task Replay_keeps_same_scene_without_scene_exit_or_reenter()
        {
            ChapterProgression chapter = CreateChapter();
            var recorder = new BoundaryRecorder();
            var boundaries = new ProgressionBoundaries(recorder, recorder, recorder);
            var session = new ChapterSession(chapter, boundaries);

            await session.EnterAsync();

            ChapterAdvance first = await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceAsync(
                first.Options[0],
                SceneChoiceSource.User,
                rollbackAnchor: 10);

            await session.CompleteCurrentEpisodeAsync();

            SceneProgression sceneBeforeReplay = session.Scene;
            ProgressionState chapterStateBeforeReplay = session.State;

            await session.ReplayAsync(rollbackAnchor: 10);

            Assert.That(session.Scene, Is.SameAs(sceneBeforeReplay));
            Assert.That(session.State, Is.SameAs(chapterStateBeforeReplay));
            Assert.That(session.Scene.IsCommitted, Is.False);
            Assert.That(session.Scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(session.Scene.HasRecordedChoice, Is.True);
            Assert.That(session.IsWaitingForAdvance, Is.False);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Chapter.Enter:chapter",
                    "Scene.Enter:scene-a",
                    "Episode.Enter:a",
                    "Episode.Exit:a",
                    "Episode.Enter:b",
                    "Episode.Exit:b",
                    "Episode.Enter:a",
                },
                recorder.Events);
        }

        [Test]
        public void Scene_keeps_entry_state_until_commit()
        {
            ChapterProgression chapter = CreateChapter();
            ProgressionState entry = chapter.CreateEntryState();
            var scene = new SceneProgression(chapter, entry);

            ChapterAdvance advance = ChapterTransition.Resolve(chapter, scene.WorkingState);
            scene.Advance(advance.Options[0], SceneChoiceSource.User, rollbackAnchor: 10);

            Assert.That(scene.EntryState.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(scene.WorkingState.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.IsCommitted, Is.False);

            ProgressionState committed = scene.Commit();

            Assert.That(committed.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(scene.IsCommitted, Is.True);
            Assert.Throws<InvalidOperationException>(() => scene.Commit());
        }

        [Test]
        public async Task RestorePath_IsUsedOnlyByFirstScene()
        {
            ChapterProgression chapter = CreateChapter();
            var recorder = new BoundaryRecorder();
            var boundaries = new ProgressionBoundaries(recorder, recorder, recorder);
            var session = new ChapterSession(chapter, boundaries);
            ProgressionState restoredState = chapter.CreateEntryState();
            var path = new[] { new ScenePathStep("a", 0) };

            await session.EnterAsync(restoredState, path);

            Assert.That(recorder.SceneEntries, Is.EqualTo(new[] { SceneEntryKind.Restore }));

            await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceRecordedAsync();

            ChapterAdvance second = await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceAsync(second.Options[0], SceneChoiceSource.User);

            Assert.That(session.Scene.SceneId, Is.EqualTo("scene-b"));
            Assert.That(
                recorder.SceneEntries,
                Is.EqualTo(new[] { SceneEntryKind.Restore, SceneEntryKind.Normal }));
        }

        [Test]
        public async Task InvalidRestorePath_EntersSceneAsNormal()
        {
            ChapterProgression chapter = CreateChapter();
            var recorder = new BoundaryRecorder();
            var boundaries = new ProgressionBoundaries(recorder, recorder, recorder);
            var session = new ChapterSession(chapter, boundaries);
            ProgressionState restoredState = chapter.CreateEntryState();
            var path = new[] { new ScenePathStep("wrong", 0) };

            await session.EnterAsync(restoredState, path);

            Assert.That(session.Scene.CurrentEpisodeId, Is.EqualTo("a"));
            Assert.That(session.Scene.RecordedChoiceCount, Is.Zero);
            Assert.That(recorder.SceneEntries, Is.EqualTo(new[] { SceneEntryKind.Normal }));
        }

        [Test]
        public async Task RecordedPath_CanAdvanceWithoutUserChoice()
        {
            ChapterProgression chapter = CreateChapter();
            var recorder = new BoundaryRecorder();
            var boundaries = new ProgressionBoundaries(recorder, recorder, recorder);
            var session = new ChapterSession(chapter, boundaries);
            ProgressionState restoredState = chapter.CreateEntryState();
            var path = new[]
            {
                new ScenePathStep("a", 0),
                new ScenePathStep("b", 0),
            };

            await session.EnterAsync(restoredState, path);

            await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceRecordedAsync(rollbackAnchor: 10);

            Assert.That(session.Scene.CurrentEpisodeId, Is.EqualTo("b"));
            Assert.That(session.Scene.HasRecordedChoice, Is.True);

            await session.CompleteCurrentEpisodeAsync();
            await session.AdvanceRecordedAsync(rollbackAnchor: 20);

            Assert.That(session.Scene.SceneId, Is.EqualTo("scene-b"));
            Assert.That(session.Scene.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(session.State.CurrentEpisodeId, Is.EqualTo("c"));
            Assert.That(session.IsWaitingForAdvance, Is.False);
            Assert.That(
                recorder.SceneEntries,
                Is.EqualTo(new[] { SceneEntryKind.Restore, SceneEntryKind.Normal }));
        }

        [Test]
        public void RestorePath_requires_restored_chapter_state()
        {
            ChapterProgression chapter = CreateChapter();
            var session = new ChapterSession(chapter);
            var path = new[] { new ScenePathStep("a", 0) };

            Assert.ThrowsAsync<ArgumentException>(
                async () => await session.EnterAsync(restorePath: path));
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
                sceneId: "scene-b");

            return new ChapterProgression(
                "chapter",
                "Chapter",
                "a",
                Array.Empty<StatDefinition>(),
                new[] { a, b, c });
        }

        private sealed class BoundaryRecorder :
            IChapterBoundary,
            ISceneBoundary,
            IEpisodeBoundary
        {
            public List<string> Events { get; } = new();
            public List<SceneEntryKind> SceneEntries { get; } = new();

            public Task EnterAsync(ChapterEnterContext context)
            {
                Events.Add($"Chapter.Enter:{context.Chapter.ChapterId}");
                return Task.CompletedTask;
            }

            public Task ExitAsync(ChapterExitContext context)
            {
                Events.Add($"Chapter.Exit:{context.Chapter.ChapterId}");
                return Task.CompletedTask;
            }

            public Task EnterAsync(SceneEnterContext context)
            {
                Events.Add($"Scene.Enter:{context.Scene.SceneId}");
                SceneEntries.Add(context.EntryKind);
                return Task.CompletedTask;
            }

            public Task ExitAsync(SceneExitContext context)
            {
                Events.Add($"Scene.Exit:{context.Scene.SceneId}");
                return Task.CompletedTask;
            }

            public Task EnterAsync(EpisodeEnterContext context)
            {
                Events.Add($"Episode.Enter:{context.Episode.EpisodeId}");
                return Task.CompletedTask;
            }

            public Task ExitAsync(EpisodeExitContext context)
            {
                Events.Add($"Episode.Exit:{context.Episode.EpisodeId}");
                return Task.CompletedTask;
            }
        }
    }
}
