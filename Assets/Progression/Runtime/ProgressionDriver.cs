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

        private ChapterDefinition _chapterDef;
        private ProgressionState _chapterState;
        private IReadOnlyList<ScenePathStep> _restorePath;

        private SceneRunContext _currentContext;
        private CancellationTokenSource _runCancellation;
        private Task _runTask = Task.CompletedTask;

        public bool IsRunning => !_runTask.IsCompleted;
        public Task Completion => _runTask;

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _currentContext?.Progress.PendingPath ?? Array.Empty<CommittedChoice>();

        public ProgressionDriver(
            SceneRunner sceneRunner,
            IChapterLifecycle chapterLifecycle,
            IProgressionReporter reporter,
            IProgressionLog log = null)
        {
            _sceneRunner = sceneRunner;
            _chapterLifecycle = chapterLifecycle;
            _reporter = reporter;
            _log = log ?? NullProgressionLog.Instance;
        }

        public void Start(
            ChapterDefinition chapter,
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
            ChapterDefinition chapter,
            ProgressionState entryState,
            IReadOnlyList<ScenePathStep> restorePath)
        {
            var cancellation = new CancellationTokenSource();

            _runCancellation = cancellation;
            _chapterDef = chapter;
            _chapterState = entryState;
            _restorePath = restorePath;

            try
            {
                _chapterLifecycle.BeginChapter(_chapterDef);
                _reporter.ReportChapterEntered(_chapterDef.ChapterId, _chapterState);

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
                throw;
            }
            finally
            {
                if (ReferenceEquals(_runCancellation, cancellation))
                    _runCancellation = null;

                _currentContext = null;
                _chapterDef = null;
                _chapterState = null;
                _restorePath = null;

                cancellation.Dispose();
            }
        }

        private async Task RunChapterAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SceneProgress progress = new(_chapterDef, _chapterState);
                IReadOnlyList<ScenePathStep> restorePath = _restorePath;
                _restorePath = null;
                
                SceneRunContext ctx = new(progress, restorePath);
                
                _currentContext = ctx;

                try
                {
                    SceneRunResult result =
                        await _sceneRunner.RunAsync(ctx, cancellationToken);

                    _chapterState = result.ExitState;

                    switch (result.Outcome)
                    {
                        case SceneRunOutcome.SceneEnded:
                            continue;

                        case SceneRunOutcome.ChapterEnded:
                            _reporter.ReportChapterExited(_chapterDef.ChapterId, _chapterState);
                            return;

                        default:
                            continue;
                    }
                }
                finally
                {
                    if (ReferenceEquals(_currentContext, ctx))
                        _currentContext = null;
                }
            }
        }

        public Task RequestReplayAsync()
        {
            SceneRunContext ctx = _currentContext;

            if (ctx == null)
                return Task.CompletedTask;

            _log.Info("[REPLAY] REQUEST — 현재 Scene을 유지하고 root부터 다시 실행한다.");
            return _sceneRunner.RequestReplayAsync(ctx);
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
