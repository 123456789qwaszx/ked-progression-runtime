using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ked.Progression.Tests
{
    public sealed class SceneRunnerTests
    {
        [Test]
        public async Task RunAsync_ChoiceAcrossScene_CommitsEntryScene()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            ProgressionState entry = chapter.CreateEntryState();

            var playback = new FakeScenePlayback();
            var reporter = new FakeProgressionReporter();
            var backlog = new FakeSceneBacklog();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                backlog);

            var ctx = new SceneRunContext(new SceneProgress(chapter, entry));
            SceneRunResult result = await runner.RunAsync(ctx, default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(result.ExitState.CurrentEpisodeId, Is.EqualTo("ep-2"));
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
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var replayState = new FakeSceneReplayState();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                replayState,
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                new FakeProgressionReporter(),
                new FakeSceneBacklog());

            var ctx = new SceneRunContext(
                new SceneProgress(chapter, chapter.CreateEntryState()),
                new[] { new ScenePathStep("ep-1", 0) });

            SceneRunResult result = await runner.RunAsync(ctx, default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(replayState.BeginLoadReplayCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RunAsync_InvalidRestorePath_DoesNotStartPresentationReplay()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var replayState = new FakeSceneReplayState();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                replayState,
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                new FakeProgressionReporter(),
                new FakeSceneBacklog());

            var ctx = new SceneRunContext(
                new SceneProgress(chapter, chapter.CreateEntryState()),
                new[] { new ScenePathStep("wrong-episode", 0) });

            SceneRunResult result = await runner.RunAsync(ctx, default);

            Assert.That(result.Outcome, Is.EqualTo(SceneRunOutcome.SceneEnded));
            Assert.That(replayState.BeginLoadReplayCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RunAsync_ReplayKeepsSameSceneWithoutCommitOrExit()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var playback = new ReplayBlockingScenePlayback();
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                new FakeSceneBacklog());

            var ctx = new SceneRunContext(
                new SceneProgress(chapter, chapter.CreateEntryState()));

            using var cancellation = new CancellationTokenSource();

            Task runTask = runner.RunAsync(ctx, cancellation.Token);
            await playback.FirstPlayStarted;

            await runner.RequestReplayAsync(ctx);
            await playback.SecondPlayStarted;

            Assert.That(ctx.ReplayPending, Is.False);
            Assert.That(ctx.Progress.CurrentEpisodeId, Is.EqualTo(ctx.Progress.RootEpisodeId));
            Assert.That(reporter.SceneEnteredCount, Is.EqualTo(1));
            Assert.That(reporter.SceneCommittedCount, Is.Zero);
            Assert.That(reporter.SceneExitedCount, Is.Zero);
            Assert.That(
                reporter.Events,
                Is.EqualTo(new[]
                {
                    "SceneEnter:scene-1",
                    "EpisodeEnter:ep-1",
                    "EpisodeEnter:ep-1",
                }));

            cancellation.Cancel();
            await runner.StopAsync();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await runTask);
        }

        [Test]
        public async Task Driver_NormalProgression_ReportsLifecycleInOrder()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();

            var playback = new FakeScenePlayback();
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                reporter);

            driver.Start(chapter, chapter.CreateEntryState());
            await driver.Completion;

            Assert.That(driver.IsRunning, Is.False);
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
        public async Task Driver_RestorePath_IsConsumedOnlyByFirstScene()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var replayState = new FakeSceneReplayState();
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                replayState,
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                reporter);

            driver.Start(
                chapter,
                chapter.CreateEntryState(),
                new[] { new ScenePathStep("ep-1", 0) });

            await driver.Completion;

            Assert.That(replayState.BeginLoadReplayCount, Is.EqualTo(1));
            Assert.That(reporter.SceneEnteredCount, Is.EqualTo(2));
            Assert.That(reporter.SceneCommittedCount, Is.EqualTo(2));
        }

        [Test]
        public async Task Driver_StopDuringEpisode_DoesNotCommitOrExitCurrentScene()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var playback = new BlockingScenePlayback();
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
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

        [Test]
        // Via가 있던 시절에는 "선택을 기록했지만 커서는 아직 안 옮긴" 창이 있었고 거기서 이것을 쟀다.
        // Via를 걷으면서 그 창은 사라졌지만 보증은 남는다 — 같은 장면의 다음 Episode를 재생하는 동안
        // Stop이 들어와도 이미 기록된 선택을 확정하지 않는다.
        public async Task Driver_StopWithPendingChoice_DoesNotCommitPendingChoice()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateWithinSceneChoiceChapter();
            var playback = new NodeBlockingScenePlayback("node-1b");
            var reporter = new FakeProgressionReporter();

            var runner = new SceneRunner(
                playback,
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                NullScenePersistence.Instance,
                reporter,
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                reporter);

            driver.Start(chapter, chapter.CreateEntryState());
            await playback.Blocked;

            Assert.That(driver.PendingPath.Count, Is.EqualTo(1));

            await driver.StopAsync();

            Assert.That(reporter.SceneCommittedCount, Is.Zero);
            Assert.That(reporter.SceneExitedCount, Is.Zero);
            Assert.That(reporter.Events, Does.Not.Contain("SceneCommit:scene-1"));
            Assert.That(reporter.Events, Does.Not.Contain("SceneExit:scene-1"));
        }

        [Test]
        public void RunAsync_PersistenceFailure_DoesNotReportCommitOrExit()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();
            var reporter = new FakeProgressionReporter();
            var persistence = new FailingScenePersistence();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                persistence,
                reporter,
                new FakeSceneBacklog());

            var ctx = new SceneRunContext(
                new SceneProgress(chapter, chapter.CreateEntryState()));

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await runner.RunAsync(ctx, default));

            Assert.That(persistence.EnterCount, Is.EqualTo(1));
            Assert.That(persistence.CommitCount, Is.EqualTo(1));
            Assert.That(reporter.SceneCommittedCount, Is.Zero);
            Assert.That(reporter.SceneExitedCount, Is.Zero);
        }

        [Test]
        public void Driver_PersistenceFailure_FaultsCompletion()
        {
            ChapterDefinition chapter = TestChapterFactory.CreateTwoSceneChapter();

            var runner = new SceneRunner(
                new FakeScenePlayback(),
                new FakeOptionsView(),
                new FakeSceneReplayState(),
                new FakeRollbackHistory(),
                new FailingScenePersistence(),
                new FakeProgressionReporter(),
                new FakeSceneBacklog());

            var driver = new ProgressionDriver(
                runner,
                new FakeProgressionReporter());

            driver.Start(chapter, chapter.CreateEntryState());

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await driver.Completion);

            Assert.That(driver.IsRunning, Is.False);
        }
    }

    internal static class TestChapterFactory
    {
        public static ChapterDefinition CreateTwoSceneChapter()
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

            return new ChapterDefinition(
                "chapter",
                "Chapter",
                "ep-1",
                Array.Empty<StatDefinition>(),
                new[] { first, second });
        }

        // 장면을 나가지 않는 선택 하나 — 고른 뒤에도 같은 Scene에 남으므로 pending이 확정되지 않는다.
        public static ChapterDefinition CreateWithinSceneChoiceChapter()
        {
            EpisodeOption toMiddle = EpisodeOption.Choice(
                choiceLabel: "next",
                targetEpisodeId: "ep-1b");

            EpisodeOption toSecond = EpisodeOption.Choice(
                choiceLabel: "out",
                targetEpisodeId: "ep-2");

            var first = new EpisodeNode(
                episodeId: "ep-1",
                title: "Episode 1",
                dialogueEntryId: "node-1",
                nextOptions: new[] { toMiddle },
                eventKey: string.Empty,
                sceneId: "scene-1");

            var middle = new EpisodeNode(
                episodeId: "ep-1b",
                title: "Episode 1b",
                dialogueEntryId: "node-1b",
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

            return new ChapterDefinition(
                "chapter",
                "Chapter",
                "ep-1",
                Array.Empty<StatDefinition>(),
                new[] { first, middle, second });
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

    // 지정한 노드 하나에서만 멈춰 선다. 그 앞의 재생은 그대로 흘려보낸다.
    internal sealed class NodeBlockingScenePlayback : FakeScenePlayback
    {
        private readonly string _blockAt;
        private readonly TaskCompletionSource<bool> _blocked = new();
        private readonly TaskCompletionSource<bool> _released = new();

        public NodeBlockingScenePlayback(string blockAt)
        {
            _blockAt = blockAt;
        }

        public Task Blocked => _blocked.Task;

        public override Task PlayNodeAsync(string nodeName)
        {
            PlayedNodes.Add(nodeName);

            if (!string.Equals(nodeName, _blockAt, StringComparison.Ordinal))
                return Task.CompletedTask;

            _blocked.TrySetResult(true);
            return _released.Task;
        }

        public override Task StopAsync()
        {
            _released.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    internal sealed class ReplayBlockingScenePlayback : FakeScenePlayback
    {
        private readonly TaskCompletionSource<bool> _firstStarted = new();
        private readonly TaskCompletionSource<bool> _secondStarted = new();
        private TaskCompletionSource<bool> _currentGate;
        private int _playCount;

        public Task FirstPlayStarted => _firstStarted.Task;
        public Task SecondPlayStarted => _secondStarted.Task;

        public override Task PlayNodeAsync(string nodeName)
        {
            PlayedNodes.Add(nodeName);
            _playCount++;
            _currentGate = new TaskCompletionSource<bool>();

            if (_playCount == 1)
                _firstStarted.TrySetResult(true);
            else if (_playCount == 2)
                _secondStarted.TrySetResult(true);

            return _currentGate.Task;
        }

        public override Task StopAsync()
        {
            _currentGate?.TrySetResult(true);
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

    internal sealed class FailingScenePersistence : IScenePersistence
    {
        public int EnterCount { get; private set; }
        public int CommitCount { get; private set; }

        public void EnterScene(
            string chapterId,
            string sceneId,
            ProgressionState entryState)
        {
            EnterCount++;
        }

        public void CommitScene(
            string chapterId,
            string sceneId,
            SceneCommitResult result,
            SceneRunOutcome outcome)
        {
            CommitCount++;
            throw new InvalidOperationException("저장 실패");
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

}
