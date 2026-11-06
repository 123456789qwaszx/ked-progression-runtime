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
        private readonly ISceneRunner _sceneRunner;
        private readonly IProgressionLog _log;

        private ChapterDefinition _chapterDef;
        private ChapterState _chapterState;
        private IReadOnlyList<ScenePathStep> _restorePath;

        private SceneRunSession _currentSession;
        private CancellationTokenSource _runCancellation;
        private Task _runTask = Task.CompletedTask;

        public bool IsRunning => !_runTask.IsCompleted;

        public IReadOnlyList<CommittedChoice> PendingPath =>
            _currentSession?.Progress.PendingPath ?? Array.Empty<CommittedChoice>();

        // 지금 이 순간의 진행 상태. 대사가 스탯을 읽는 유일한 통로다.
        //
        // 실행 중에는 WorkingState를 준다 — 간선 관문(ChapterTransition)이 보는 것과 같은 값이다.
        // 둘이 다르면 "선택지는 잠겼는데 대사 조건은 통과"가 생기고, 그것은 화면에서 버그로 보이지 않는다.
        //
        // Scene 경계 사이처럼 실행 중인 Scene이 없으면 마지막 확정 상태를 준다.
        // 실행 자체가 없으면 null이다 — 호출자가 그 뜻을 정한다.
        public ChapterState CurrentState =>
            _currentSession?.Progress.EffectiveState ?? _chapterState;

        public ProgressionDriver(
            ISceneRunner sceneRunner,
            IProgressionLog log = null)
        {
            _sceneRunner = sceneRunner;
            _log = log;
        }

        public void Start(
            ChapterDefinition chapterDef,
            ChapterState chapterState,
            IReadOnlyList<ScenePathStep> restorePath = null)
        {
            if (IsRunning)
            {
                _log?.Warning("[RUN] 이미 실행 중이다. 새 요청을 무시한다.");
                return;
            }

            _runTask = RunAsync(chapterDef, chapterState, restorePath);
        }

        private async Task RunAsync(
            ChapterDefinition chapterDef,
            ChapterState chapterState,
            IReadOnlyList<ScenePathStep> restorePath)
        {
            var cancellation = new CancellationTokenSource();

            _runCancellation = cancellation;
            _chapterDef = chapterDef;
            _chapterState = chapterState;
            _restorePath = restorePath;

            try
            {
                await RunChapterAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                _log?.Info("[RUN] CANCELLED — 현재 Scene pending은 commit하지 않는다.");
            }
            catch (Exception error)
            {
                _log?.Error($"[RUN] FAULTED\n{error}");
                throw;
            }
            finally
            {
                if (ReferenceEquals(_runCancellation, cancellation))
                    _runCancellation = null;

                _currentSession = null;
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
                
                SceneRunSession session =
                    restorePath == null
                        ? SceneRunSession.StartNew(progress)
                        : SceneRunSession.Restore(progress, restorePath);

                _currentSession = session;

                try
                {
                    SceneRunResult result =
                        await _sceneRunner.RunAsync(session, cancellationToken);

                    _chapterState = result.ExitState;

                    switch (result.Outcome)
                    {
                        case SceneRunOutcome.SceneEnded:
                            continue;

                        case SceneRunOutcome.ChapterEnded:
                            return;

                        default:
                            continue;
                    }
                }
                finally
                {
                    if (ReferenceEquals(_currentSession, session))
                        _currentSession = null;
                }
            }
        }

        public Task RequestReplayAsync()
        {
            SceneRunSession session = _currentSession;

            if (session == null)
                return Task.CompletedTask;

            _log?.Info("[REPLAY] REQUEST — 현재 Scene을 유지하고 root부터 다시 실행한다.");
            return _sceneRunner.RequestReplayAsync(session);
        }

        public async Task StopAsync()
        {
            CancellationTokenSource cancellation = _runCancellation;
            Task runTask = _runTask;

            if (!IsRunning || cancellation == null)
                return;

            _log?.Info("[RUN] STOP REQUEST");
            cancellation.Cancel();

            await Task.WhenAll(
                _sceneRunner.StopAsync(),
                runTask);
        }
    }
}
