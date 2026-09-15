using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression
{
    // Chapter 실행 경계.
    // - Chapter lifecycle 진입/종료
    // - SceneTransaction 반복 실행
    // - Scene commit 결과를 다음 Scene entry state로 전달
    // - progression 전체 cancellation 소유
    public sealed class ProgressionDriver
    {
        private readonly SceneRunner _sceneRunner;
        private readonly IChapterLifecycle _chapterLifecycle;
        private readonly IProgressionReporter _reporter;
        private readonly IProgressionLog _log;

        private ChapterProgression _chapter;
        private ProgressionState _state;
        private IReadOnlyList<ScenePathStep> _restorePath;

        private SceneTransaction _currentScene;
        private CancellationTokenSource _runCancellation;
        private Task _runTask = Task.CompletedTask;

        public bool IsRunning => !_runTask.IsCompleted;
        public Task Completion => _runTask;

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _currentScene?.PendingPath ?? Array.Empty<CommittedChoice>();

        public ProgressionDriver(
            SceneRunner sceneRunner,
            IChapterLifecycle chapterLifecycle,
            IProgressionReporter reporter,
            IProgressionLog log = null)
        {
            _sceneRunner = sceneRunner ?? throw new ArgumentNullException(nameof(sceneRunner));
            _chapterLifecycle = chapterLifecycle ?? throw new ArgumentNullException(nameof(chapterLifecycle));
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _log = log ?? NullProgressionLog.Instance;
        }

        public void Start(
            ChapterProgression chapter,
            ProgressionState entryState,
            IReadOnlyList<ScenePathStep> restorePath = null)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            if (entryState == null)
                throw new ArgumentNullException(nameof(entryState));

            if (IsRunning)
            {
                _log.Warning("[RUN] 이미 실행 중이다. 새 요청을 무시한다.");
                return;
            }

            _runTask = RunAsync(chapter, entryState, restorePath);
        }

        private async Task RunAsync(
            ChapterProgression chapter,
            ProgressionState entryState,
            IReadOnlyList<ScenePathStep> restorePath)
        {
            var cancellation = new CancellationTokenSource();

            _runCancellation = cancellation;
            _chapter = chapter;
            _state = entryState;
            _restorePath = restorePath;

            try
            {
                _log.Info($"[RUN] START chapter={_chapter.ChapterId} episode={_state.CurrentEpisodeId}");

                _chapterLifecycle.BeginChapter(_chapter);
                _reporter.ReportChapterEntered(_chapter.ChapterId, _state);

                await RunChapterAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                _log.Info("[RUN] CANCELLED — 현재 Scene pending은 commit하지 않는다.");
            }
            catch (Exception error)
            {
                _log.Error($"[RUN] FAULTED\n{error}");
            }
            finally
            {
                if (ReferenceEquals(_runCancellation, cancellation))
                    _runCancellation = null;

                _currentScene = null;
                _chapter = null;
                _state = null;
                _restorePath = null;

                cancellation.Dispose();
            }
        }

        private async Task RunChapterAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<ScenePathStep> restorePath = _restorePath;
                _restorePath = null;

                var scene = new SceneTransaction(
                    _chapter,
                    _state,
                    restorePath);

                _currentScene = scene;

                try
                {
                    SceneRunResult result =
                        await _sceneRunner.RunAsync(scene, cancellationToken);

                    _state = result.State;

                    switch (result.Outcome)
                    {
                        case SceneRunOutcome.SceneEnded:
                            continue;

                        case SceneRunOutcome.ChapterEnded:
                            _reporter.ReportChapterExited(
                                _chapter.ChapterId,
                                _state);
                            return;

                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(result.Outcome),
                                result.Outcome,
                                "알 수 없는 장면 실행 결과다.");
                    }
                }
                finally
                {
                    if (ReferenceEquals(_currentScene, scene))
                        _currentScene = null;
                }
            }
        }

        public Task RequestReplayAsync()
        {
            SceneTransaction scene = _currentScene;

            if (scene == null)
                return Task.CompletedTask;

            _log.Info("[REPLAY] REQUEST — 현재 Scene을 유지하고 root부터 다시 실행한다.");
            return _sceneRunner.RequestReplayAsync(scene);
        }

        public async Task StopAsync()
        {
            CancellationTokenSource cancellation = _runCancellation;
            Task runTask = _runTask;

            if (!IsRunning || cancellation == null)
                return;

            _log.Info("[RUN] STOP REQUEST");
            cancellation.Cancel();

            await Task.WhenAll(
                _sceneRunner.StopAsync(),
                runTask);
        }
    }
}
