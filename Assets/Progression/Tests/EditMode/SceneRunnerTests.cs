using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class SceneRunnerTests
    {
        [Test]
        public async Task RunAsync_ChoiceAcrossScene_CommitsEntryScene()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            ProgressionState entry = chapter.CreateEntryState();

            var playback = new FakeScenePlayback();
            var options = new FakeOptionsView();
            var replayState = new FakeSceneReplayState();
            var rollback = new FakeRollbackHistory();
            var reporter = new FakeProgressionReporter();
            var backlog = new FakeSceneBacklog();

            var runner = new SceneRunner(
                playback,
                options,
                replayState,
                rollback,
                reporter,
                backlog);

            SceneRunResult result = await runner.RunAsync(
                new SceneTransaction(chapter, entry),
                default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(result.State.CurrentEpisodeId, Is.EqualTo("ep-2"));
            Assert.That(playback.PlayedNodes, Is.EqualTo(new[] { "node-1" }));
            Assert.That(backlog.SceneStartCount, Is.EqualTo(1));
            Assert.That(reporter.SceneEnteredCount, Is.EqualTo(1));
            Assert.That(reporter.SceneCommittedCount, Is.EqualTo(1));
            Assert.That(reporter.SceneExitedCount, Is.EqualTo(1));
            Assert.That(reporter.LastChoices.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task RunAsync_RestorePath_StartsPresentationReplayAfterPathValidation()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            var replayState = new FakeSceneReplayState();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                replayState,
                new FakeRollbackHistory(),
                new FakeProgressionReporter(),
                new FakeSceneBacklog());

            SceneRunResult result = await runner.RunAsync(
                new SceneTransaction(
                    chapter,
                    chapter.CreateEntryState(),
                    new[] { new ScenePathStep("ep-1", 0) }),
                default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(replayState.BeginLoadReplayCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RunAsync_InvalidRestorePath_DoesNotStartPresentationReplay()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            var replayState = new FakeSceneReplayState();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                replayState,
                new FakeRollbackHistory(),
                new FakeProgressionReporter(),
                new FakeSceneBacklog());

            SceneRunResult result = await runner.RunAsync(
                new SceneTransaction(
                    chapter,
                    chapter.CreateEntryState(),
                    new[] { new ScenePathStep("wrong-episode", 0) }),
                default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(replayState.BeginLoadReplayCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Driver_NormalProgression_ReportsLifecycleInOrder()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();

            var playback = new FakeScenePlayback();
            var reporter = new FakeProgressionReporter();
            var lifecycle = new FakeChapterLifecycle();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                lifecycle,
                reporter);

            driver.Start(chapter, chapter.CreateEntryState());
            await driver.Completion;

            Assert.That(driver.IsRunning, Is.False);
            Assert.That(lifecycle.BeginCount, Is.EqualTo(1));
            Assert.That(playback.PlayedNodes, Is.EqualTo(new[] { "node-1", "node-2" }));
            Assert.That(reporter.SceneCommittedCount, Is.EqualTo(2));
            Assert.That(
                reporter.Events,
                Is.EqualTo(new[]
                {
                    "ChapterEnter:chapter",
                    "SceneEnter:scene-1",
                    "EpisodeEnter:ep-1",
                    "EpisodeExit:ep-1",
                    "SceneCommit:scene-1",
                    "SceneExit:scene-1",
                    "SceneEnter:scene-2",
                    "EpisodeEnter:ep-2",
                    "EpisodeExit:ep-2",
                    "SceneCommit:scene-2",
                    "SceneExit:scene-2",
                    "ChapterExit:chapter",
                }));
        }

        [Test]
        public async Task Driver_Stop_DoesNotCommitOrExitCurrentScene()
        {
            ChapterProgression chapter = TestChapterFactory.CreateTwoSceneChapter();
            var playback = new BlockingScenePlayback();
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                new FakeChapterLifecycle(),
                reporter);

            driver.Start(chapter, chapter.CreateEntryState());
            await playback.PlayStarted;

            await driver.StopAsync();

            Assert.That(reporter.Events, Does.Contain("ChapterEnter:chapter"));
            Assert.That(reporter.Events, Does.Contain("SceneEnter:scene-1"));
            Assert.That(reporter.Events, Does.Contain("EpisodeEnter:ep-1"));
            Assert.That(reporter.Events, Does.Not.Contain("EpisodeExit:ep-1"));
            Assert.That(reporter.Events, Does.Not.Contain("SceneCommit:scene-1"));
            Assert.That(reporter.Events, Does.Not.Contain("SceneExit:scene-1"));
            Assert.That(reporter.Events, Does.Not.Contain("ChapterExit:chapter"));
        }
    }

    internal static class TestChapterFactory
    {
        public static ChapterProgression CreateTwoSceneChapter()
        {
            EpisodeOption toSecond = EpisodeOption.Choice(
                choiceLabel: "next",
                targetEpisodeId: "ep-2");

            var first = new EpisodeNode(
                episodeId: "ep-1",
                title: "Episode 1",
                dialogueEntryId: "node-1",
                nextOptions: new[] { toSecond },
                eventKey: string.Empty,
                sceneId: "scene-1");

            var second = new EpisodeNode(
                episodeId: "ep-2",
                title: "Episode 2",
                dialogueEntryId: "node-2",
                nextOptions: Array.Empty<EpisodeOption>(),
                eventKey: string.Empty,
                sceneId: "scene-2");

            return new ChapterProgression(
                "chapter",
                "Chapter",
                "ep-1",
                Array.Empty<StatDefinition>(),
                new[] { first, second });
        }
    }

    internal class FakeScenePlayback : IScenePlayback
    {
        public List<string> PlayedNodes { get; } = new();

        public virtual Task BeginSceneAsync() => Task.CompletedTask;

        public virtual Task PlayNodeAsync(string nodeName)
        {
            PlayedNodes.Add(nodeName);
            return Task.CompletedTask;
        }

        public virtual Task PrepareReplayAsync() => Task.CompletedTask;
        public virtual Task StopAsync() => Task.CompletedTask;
    }

    internal sealed class BlockingScenePlayback : FakeScenePlayback
    {
        private readonly TaskCompletionSource<bool> _started = new();
        private readonly TaskCompletionSource<bool> _stopped = new();

        public Task PlayStarted => _started.Task;

        public override Task PlayNodeAsync(string nodeName)
        {
            PlayedNodes.Add(nodeName);
            _started.TrySetResult(true);
            return _stopped.Task;
        }

        public override Task StopAsync()
        {
            _stopped.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeOptionsView : IChapterOptionsView
    {
        public Task<int> ShowAsync(IReadOnlyList<ResolvedOption> options, int hiddenCount) =>
            Task.FromResult(0);

        public void Cancel()
        {
        }
    }

    internal sealed class FakeSceneReplayState : ISceneReplayState
    {
        public bool IsSeekingActive { get; private set; }
        public int BeginLoadReplayCount { get; private set; }

        public void BeginLoadReplay()
        {
            BeginLoadReplayCount++;
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
        public List<string> Events { get; } = new();

        public int SceneEnteredCount { get; private set; }
        public int SceneCommittedCount { get; private set; }
        public int SceneExitedCount { get; private set; }

        public IReadOnlyList<CommittedChoice> LastChoices { get; private set; } =
            Array.Empty<CommittedChoice>();

        public void ReportChapterEntered(string chapterId, ProgressionState state)
        {
            Events.Add($"ChapterEnter:{chapterId}");
        }

        public void ReportChapterExited(string chapterId, ProgressionState state)
        {
            Events.Add($"ChapterExit:{chapterId}");
        }

        public void ReportSceneEntered(
            string chapterId,
            string sceneId,
            ProgressionState entryState)
        {
            SceneEnteredCount++;
            Events.Add($"SceneEnter:{sceneId}");
        }

        public void ReportSceneCommitted(
            string chapterId,
            string sceneId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state)
        {
            SceneCommittedCount++;
            LastChoices = choices;
            Events.Add($"SceneCommit:{sceneId}");
        }

        public void ReportSceneExited(
            string chapterId,
            string sceneId,
            ProgressionState committedState)
        {
            SceneExitedCount++;
            Events.Add($"SceneExit:{sceneId}");
        }

        public void ReportEpisodeEntered(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
            Events.Add($"EpisodeEnter:{episode.EpisodeId}");
        }

        public void ReportEpisodeExited(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
            Events.Add($"EpisodeExit:{episode.EpisodeId}");
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

    internal sealed class FakeChapterLifecycle : IChapterLifecycle
    {
        public int BeginCount { get; private set; }

        public void BeginChapter(ChapterProgression chapter)
        {
            BeginCount++;
        }
    }
}
