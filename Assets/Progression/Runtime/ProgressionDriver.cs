using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ked.Progression
{
    // Chapter 실행 경계.
    // - Chapter lifecycle 진입
    // - SceneTransaction 반복 실행
    // - Scene commit 결과를 다음 Scene entry state로 전달
    // - progression 전체 cancellation 소유
    public sealed class ProgressionDriver
    {
        private readonly SceneRunner _sceneRunner;
        private readonly IChapterLifecycle _chapterLifecycle;
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
            IProgressionLog log = null)
        {
            _sceneRunner = sceneRunner ?? throw new ArgumentNullException(nameof(sceneRunner));
            _chapterLifecycle = chapterLifecycle ?? throw new ArgumentNullException(nameof(chapterLifecycle));
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
                _log.Warning("[진행] 이미 돌고 있다. 새 요청을 무시한다.");
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
                _chapterLifecycle.BeginChapter(_chapter);
                await RunChapterAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                _log.Info("[진행] 취소됨.");
            }
            catch (Exception error)
            {
                _log.Error($"[진행] 멈췄다\n{error}");
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

            return _sceneRunner.RequestReplayAsync(scene);
        }

        public async Task StopAsync()
        {
            CancellationTokenSource cancellation = _runCancellation;
            Task runTask = _runTask;

            if (!IsRunning || cancellation == null)
                return;

            cancellation.Cancel();

            await Task.WhenAll(
                _sceneRunner.StopAsync(),
                runTask);
        }
    }
}
