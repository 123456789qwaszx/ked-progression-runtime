using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class SceneRunnerTests
    {
        [Test]
        public async Task RunAsync_AutoAdvanceAcrossScene_CommitsEntryScene()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneAutoChapter();
            ProgressionState entry = chapter.CreateEntryState();

            var playback = new FakeScenePlayback();
            var options = new FakeOptionsView();
            var seek = new FakeSceneSeek();
            var rollback = new FakeRollbackHistory();
            var reporter = new FakeProgressionReporter();
            var backlog = new FakeSceneBacklog();
            var dialogue = new FakeDialogueChoiceReplay();

            var runner = new SceneRunner(
                playback,
                options,
                seek,
                rollback,
                reporter,
                backlog,
                dialogue);

            SceneRunResult result = await runner.RunAsync(
                new SceneTransaction(chapter, entry),
                default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(result.State.CurrentEpisodeId, Is.EqualTo("ep-2"));
            Assert.That(playback.PlayedNodes, Is.EqualTo(new[] { "node-1" }));
            Assert.That(backlog.SceneStartCount, Is.EqualTo(1));
            Assert.That(reporter.EnteredCount, Is.EqualTo(1));
            Assert.That(reporter.CommittedCount, Is.EqualTo(1));
            Assert.That(reporter.LastChoices.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Driver_RunsScenesUntilChapterEnds()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneAutoChapter();

            var playback = new FakeScenePlayback();
            var reporter = new FakeProgressionReporter();
            var lifecycle = new FakeChapterLifecycle();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneSeek(),
                new FakeRollbackHistory(),
                reporter,
                new FakeSceneBacklog(),
                new FakeDialogueChoiceReplay());

            var driver = new ProgressionDriver(runner, lifecycle);

            driver.Start(chapter, chapter.CreateEntryState());
            await driver.Completion;

            Assert.That(driver.IsRunning, Is.False);
            Assert.That(lifecycle.BeginCount, Is.EqualTo(1));
            Assert.That(playback.PlayedNodes, Is.EqualTo(new[] { "node-1", "node-2" }));
            Assert.That(reporter.CommittedCount, Is.EqualTo(2));
        }
    }

    internal static class TestChapterFactory
    {
        public static ChapterProgression CreateTwoSceneAutoChapter()
        {
            var toSecond = new EpisodeOption(
                choiceLabel: string.Empty,
                targetEpisodeId: "ep-2",
                viaNodeId: string.Empty,
                displayConditions: Array.Empty<ProgressionCondition>(),
                enableConditions: Array.Empty<ProgressionCondition>(),
                statChanges: Array.Empty<StatChange>());

            var first = new EpisodeNode(
                episodeId: "ep-1",
                sceneId: "scene-1",
                dialogueEntryId: "node-1",
                eventKey: string.Empty,
                nextOptions: new[] { toSecond });

            var second = new EpisodeNode(
                episodeId: "ep-2",
                sceneId: "scene-2",
                dialogueEntryId: "node-2",
                eventKey: string.Empty,
                nextOptions: Array.Empty<EpisodeOption>());

            return new ChapterProgression(
                "chapter",
                "Chapter",
                "ep-1",
                Array.Empty<StatDefinition>(),
                new[] { first, second });
        }
    }

    internal sealed class FakeScenePlayback : IScenePlayback
    {
        public List<string> PlayedNodes { get; } = new();

        public Task BeginSceneAsync() => Task.CompletedTask;

        public Task PlayNodeAsync(string nodeName)
        {
            PlayedNodes.Add(nodeName);
            return Task.CompletedTask;
        }

        public Task PrepareReplayAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    internal sealed class FakeOptionsView : IChapterOptionsView
    {
        public Task<int> ShowAsync(IReadOnlyList<ResolvedOption> options, int hiddenCount) =>
            Task.FromResult(0);

        public void Cancel()
        {
        }
    }

    internal sealed class FakeSceneSeek : ISceneSeek
    {
        public bool IsSeekingActive { get; private set; }

        public void BeginLoadSeek(string nodeName, string lineId, int occurrence)
        {
            IsSeekingActive = true;
        }

        public void ClearSeek()
        {
            IsSeekingActive = false;
        }
    }

    internal sealed class FakeRollbackHistory : IRollbackHistory
    {
        public int LastHistoryIndex { get; set; }

        public bool TryTakeRollbackTarget(out int historyIndex)
        {
            historyIndex = -1;
            return false;
        }
    }

    internal sealed class FakeProgressionReporter : IProgressionReporter
    {
        public int EnteredCount { get; private set; }
        public int CommittedCount { get; private set; }
        public IReadOnlyList<CommittedChoice> LastChoices { get; private set; } = Array.Empty<CommittedChoice>();

        public void ReportSceneEntered(string chapterId, ProgressionState entryState)
        {
            EnteredCount++;
        }

        public void ReportSceneCommitted(
            string chapterId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state)
        {
            CommittedCount++;
            LastChoices = choices;
        }
    }

    internal sealed class FakeSceneBacklog : ISceneBacklog
    {
        public int SceneStartCount { get; private set; }

        public void MarkSceneStart()
        {
            SceneStartCount++;
        }
    }

    internal sealed class FakeDialogueChoiceReplay : IDialogueChoiceReplay
    {
        public void RestoreChoices(IReadOnlyList<int> choices)
        {
        }
    }

    internal sealed class FakeChapterLifecycle : IChapterLifecycle
    {
        public int BeginCount { get; private set; }

        public void BeginChapter(ChapterProgression chapter)
        {
            BeginCount++;
        }
    }
}
