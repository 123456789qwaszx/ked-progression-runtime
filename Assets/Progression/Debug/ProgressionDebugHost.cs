using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Ked.Progression.Debugging
{
    // SampleScene에서 Progression 생명주기를 직접 눌러 확인하기 위한 디버그 호스트.
    // 실제 게임 Presentation/Save 구현을 흉내 내지 않고 각 contract의 최소 동작만 제공한다.
    public sealed class ProgressionDebugHost : MonoBehaviour,
        IScenePlayback,
        IChapterOptionsView,
        ISceneReplayState,
        IRollbackHistory,
        IProgressionReporter,
        ISceneBacklog,
        IChapterLifecycle,
        IProgressionLog
    {
        private ChapterDefinition _chapter;
        private ProgressionDriver _driver;

        private TaskCompletionSource<bool> _nodeGate;
        private TaskCompletionSource<int> _choiceGate;
        private IReadOnlyList<ResolvedOption> _currentOptions = Array.Empty<ResolvedOption>();
        private int _hiddenChoiceCount;

        private ProgressionState _restoreFixtureState;
        private IReadOnlyList<ScenePathStep> _restoreFixturePath = Array.Empty<ScenePathStep>();
        private int? _rollbackTarget;

        private string _currentChapterId = "-";
        private string _currentSceneId = "-";
        private string _currentEpisodeId = "-";
        private string _currentNode = "-";

        public bool IsSeekingActive { get; private set; }
        public int LastHistoryIndex { get; private set; }

        public ProgressionDebugSnapshot CurrentSnapshot => CreateSnapshot();
        public IReadOnlyList<ResolvedOption> CurrentOptions => _currentOptions;
        public int HiddenChoiceCount => _hiddenChoiceCount;

        public event Action<ProgressionDebugSnapshot> StateChanged;
        public event Action<IReadOnlyList<ResolvedOption>, int> ChoicesChanged;

        private void Awake()
        {
            _chapter = CreateDebugChapter();

            // Continue / Manual Load의 mid-Scene 복원 계약을 반복해서 시험하기 위한 고정 fixture.
            // 저장 checkpoint는 Scene B root(ep-b1)이고, 저장 위치까지의 progression path는
            // ep-b1의 첫 선택을 재소비하여 ep-b2로 이동하는 한 단계다.
            // Yarn choice / line target은 Progression 밖의 책임이므로 여기서는 모델링하지 않는다.
            _restoreFixtureState = ProgressionState.Restore(
                _chapter,
                "ep-b1",
                new Dictionary<string, int>());

            _restoreFixturePath = new[]
            {
                new ScenePathStep("ep-b1", 0),
            };

            var runner = new SceneRunner(
                this,
                this,
                this,
                this,
                this,
                this,
                this);

            _driver = new ProgressionDriver(
                runner,
                this,
                this,
                this);

            Info("[HOST] Progression Debug Host ready");
            PublishState();
        }

        // ------------------------------------------------------------------
        // UI commands
        // ------------------------------------------------------------------

        public void RequestNewGame() => Run(NewGameAsync);

        public void RequestContinue() => Run(ContinueAsync);

        public void RequestManualLoad() => Run(ManualLoadAsync);

        public void RequestStop() => Run(StopAsync);

        public void RequestCompleteNode()
        {
            CompleteCurrentNode("normal complete");
        }

        public void RequestEpisodeSkip()
        {
            Info("[PRESENT] EPISODE SKIP REQUEST — progression cursor를 직접 바꾸지 않는다.");
            CompleteCurrentNode("episode skip");
        }

        public void RequestRollback()
        {
            Run(() => ReplayAsync(
                Math.Max(0, LastHistoryIndex - 1),
                "Rollback"));
        }

        public void RequestBacklogJump()
        {
            Run(() => ReplayAsync(
                Math.Max(0, LastHistoryIndex - 2),
                "Backlog Jump"));
        }

        public void SelectChoice(int index)
        {
            if (_choiceGate == null || _choiceGate.Task.IsCompleted)
            {
                Warning("[PRESENT] choice ignored — 열린 선택지가 없다.");
                return;
            }

            if ((uint)index >= (uint)_currentOptions.Count)
            {
                Warning($"[PRESENT] choice ignored — index={index}");
                return;
            }

            if (!_currentOptions[index].IsSelectable)
            {
                Warning($"[PRESENT] choice ignored — index={index} is not selectable");
                return;
            }

            _choiceGate.TrySetResult(index);
        }

        private async Task NewGameAsync()
        {
            Info("[HOST] NEW GAME REQUEST");
            await StopCurrentRunAsync();
            ResetTransientPlaybackState();

            _driver.Start(
                _chapter,
                _chapter.CreateEntryState());

            PublishState();
        }

        private Task ContinueAsync()
        {
            Info("[HOST] CONTINUE REQUEST");

            if (_driver.IsRunning)
            {
                Warning("[HOST] Continue는 실행이 없을 때만 시작한다.");
                return Task.CompletedTask;
            }

            ResetTransientPlaybackState();
            StartRestoreFixture("CONTINUE");
            PublishState();
            return Task.CompletedTask;
        }

        private async Task ManualLoadAsync()
        {
            Info("[HOST] MANUAL LOAD REQUEST");
            await StopCurrentRunAsync();
            ResetTransientPlaybackState();

            StartRestoreFixture("MANUAL LOAD");
            PublishState();
        }

        private void StartRestoreFixture(string source)
        {
            Info(
                $"[HOST] {source} restore fixture — " +
                $"root={_restoreFixtureState.CurrentEpisodeId} " +
                $"path={_restoreFixturePath.Count}");

            _driver.Start(
                _chapter,
                _restoreFixtureState,
                _restoreFixturePath);
        }

        private async Task StopAsync()
        {
            Info("[HOST] TITLE EXIT / STOP REQUEST");
            await StopCurrentRunAsync();
            PublishState();
        }

        private async Task StopCurrentRunAsync()
        {
            if (!_driver.IsRunning)
                return;

            await _driver.StopAsync();
            Info("[HOST] previous run discarded");
            PublishState();
        }

        private async Task ReplayAsync(int target, string source)
        {
            if (!_driver.IsRunning)
            {
                Warning($"[REPLAY] {source} ignored — 실행 중인 Scene이 없다.");
                return;
            }

            _rollbackTarget = target;
            IsSeekingActive = true;
            PublishState();

            Info($"[REPLAY] {source.ToUpperInvariant()} target={target}");
            await _driver.RequestReplayAsync();
        }

        private void ResetTransientPlaybackState()
        {
            LastHistoryIndex = 0;
            _rollbackTarget = null;
            IsSeekingActive = false;
            _currentSceneId = "-";
            _currentEpisodeId = "-";
            _currentNode = "-";
            _currentOptions = Array.Empty<ResolvedOption>();
            _hiddenChoiceCount = 0;
            _nodeGate = null;
            _choiceGate = null;

            PublishChoices();
            PublishState();
        }

        private void CompleteCurrentNode(string source)
        {
            if (_nodeGate == null || _nodeGate.Task.IsCompleted)
            {
                Warning($"[PRESENT] {source} ignored — 재생 중인 node가 없다.");
                return;
            }

            Info($"[PRESENT] node complete ({source}) — {_currentNode}");
            _nodeGate.TrySetResult(true);
        }

        private static void Run(Func<Task> action)
        {
            _ = RunSafeAsync(action);
        }

        private static async Task RunSafeAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
            }
        }

        private ProgressionDebugSnapshot CreateSnapshot()
        {
            return new ProgressionDebugSnapshot(
                _driver?.IsRunning == true,
                _currentChapterId,
                _currentSceneId,
                _currentEpisodeId,
                _currentNode,
                LastHistoryIndex,
                _driver?.PendingPath.Count ?? 0,
                IsSeekingActive,
                _restoreFixtureState?.CurrentEpisodeId ?? "-");
        }

        private void PublishState()
        {
            StateChanged?.Invoke(CreateSnapshot());
        }

        private void PublishChoices()
        {
            ChoicesChanged?.Invoke(_currentOptions, _hiddenChoiceCount);
        }

        // ------------------------------------------------------------------
        // IScenePlayback
        // ------------------------------------------------------------------

        public Task BeginSceneAsync()
        {
            Info("[PRESENT] Scene playback prepare");
            return Task.CompletedTask;
        }

        public async Task PlayNodeAsync(string nodeName)
        {
            _currentNode = nodeName;
            _nodeGate = new TaskCompletionSource<bool>();
            PublishState();

            Info($"[PRESENT] NODE START {nodeName}");

            bool completedNormally = await _nodeGate.Task;

            if (completedNormally)
            {
                LastHistoryIndex++;
                Info($"[PRESENT] NODE COMPLETE {nodeName} history={LastHistoryIndex}");
            }
            else
            {
                Info($"[PRESENT] NODE INTERRUPTED {nodeName}");
            }

            _currentNode = "-";
            PublishState();
        }

        public Task PrepareReplayAsync()
        {
            Info("[REPLAY] PREPARE — 같은 Scene checkpoint를 복원한다고 가정한다.");
            return Task.CompletedTask;
        }

        Task IScenePlayback.StopAsync()
        {
            Info("[PRESENT] PLAYBACK STOP");

            _nodeGate?.TrySetResult(false);
            _choiceGate?.TrySetCanceled();

            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------
        // IChapterOptionsView
        // ------------------------------------------------------------------

        public async Task<int> ShowAsync(
            IReadOnlyList<ResolvedOption> options,
            int hiddenCount)
        {
            _currentOptions = options;
            _hiddenChoiceCount = hiddenCount;
            _choiceGate = new TaskCompletionSource<int>();
            PublishChoices();

            Info($"[PRESENT] CHOICE OPEN visible={options.Count} hidden={hiddenCount}");

            try
            {
                return await _choiceGate.Task;
            }
            finally
            {
                _currentOptions = Array.Empty<ResolvedOption>();
                _hiddenChoiceCount = 0;
                _choiceGate = null;
                PublishChoices();
            }
        }

        public void Cancel()
        {
            _choiceGate?.TrySetCanceled();
        }

        // ------------------------------------------------------------------
        // ISceneReplayState / IRollbackHistory
        // ------------------------------------------------------------------

        public void BeginLoadReplay()
        {
            IsSeekingActive = true;
            PublishState();
            Info("[REPLAY] LOAD REPLAY BEGIN");
        }

        public void ClearSeek()
        {
            IsSeekingActive = false;
            PublishState();
            Info("[REPLAY] SEEK COMPLETE / CLEAR");
        }

        public bool TryTakeRollbackTarget(out int historyIndex)
        {
            if (_rollbackTarget.HasValue)
            {
                historyIndex = _rollbackTarget.Value;
                _rollbackTarget = null;
                Info($"[REPLAY] REWIND after={historyIndex}");
                return true;
            }

            historyIndex = -1;
            return false;
        }

        // ------------------------------------------------------------------
        // Lifecycle reporter
        // ------------------------------------------------------------------

        public void ReportChapterEntered(string chapterId, ProgressionState state)
        {
            _currentChapterId = chapterId;
            PublishState();
            Info($"[LIFE][CHAPTER] ENTER chapter={chapterId} episode={state.CurrentEpisodeId}");
        }

        public void ReportChapterExited(string chapterId, ProgressionState state)
        {
            PublishState();
            Info($"[LIFE][CHAPTER] EXIT chapter={chapterId} episode={state.CurrentEpisodeId}");
        }

        public void ReportSceneEntered(
            string chapterId,
            string sceneId,
            ProgressionState entryState)
        {
            _currentSceneId = sceneId;
            PublishState();
            Info($"[LIFE][SCENE] ENTER scene={sceneId} root={entryState.CurrentEpisodeId}");
        }

        public void ReportSceneCommitted(
            string chapterId,
            string sceneId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state)
        {
            PublishState();

            Info(
                $"[LIFE][SCENE] COMMIT scene={sceneId} " +
                $"choices={choices.Count} watched={watchedEpisodeIds.Count}");

            Info(
                $"[STATE] committed episode={state.CurrentEpisodeId} " +
                $"pendingPath={choices.Count}");
        }

        public void ReportSceneExited(
            string chapterId,
            string sceneId,
            ProgressionState committedState)
        {
            PublishState();
            Info($"[LIFE][SCENE] EXIT scene={sceneId}");
        }

        public void ReportEpisodeEntered(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
            _currentEpisodeId = episode.EpisodeId;
            PublishState();
            Info($"[LIFE][EPISODE] ENTER episode={episode.EpisodeId} scene={sceneId}");
        }

        public void ReportEpisodeExited(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
            PublishState();
            Info($"[LIFE][EPISODE] EXIT episode={episode.EpisodeId} scene={sceneId}");
        }

        // ------------------------------------------------------------------
        // Host boundary stubs
        // ------------------------------------------------------------------

        public void MarkSceneStart()
        {
            Info("[BOUNDARY][SCENE] backlog scene marker");
        }

        public void BeginChapter(ChapterDefinition chapter)
        {
            Info($"[BOUNDARY][CHAPTER] prepare chapter={chapter.ChapterId}");
        }

        // ------------------------------------------------------------------
        // IProgressionLog
        // ------------------------------------------------------------------

        public void Info(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        public void Warning(string message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        public void Error(string message)
        {
            UnityEngine.Debug.LogError(message);
        }

        private static ChapterDefinition CreateDebugChapter()
        {
            EpisodeOption a1ToA2 = EpisodeOption.Choice(
                "Stay in Scene A",
                "ep-a2");

            EpisodeOption a1ToB1 = EpisodeOption.Choice(
                "Jump to Scene B",
                "ep-b1");

            EpisodeOption a2ToB1 = EpisodeOption.Choice(
                "Finish Scene A",
                "ep-b1");

            EpisodeOption b1ToB2 = EpisodeOption.Choice(
                "Stay in Scene B",
                "ep-b2");

            var a1 = new EpisodeNode(
                "ep-a1",
                "Scene A / Episode 1",
                "node-a1",
                new[] { a1ToA2, a1ToB1 },
                "watch-a1",
                "scene-a");

            var a2 = new EpisodeNode(
                "ep-a2",
                "Scene A / Episode 2",
                "node-a2",
                new[] { a2ToB1 },
                "watch-a2",
                "scene-a");

            var b1 = new EpisodeNode(
                "ep-b1",
                "Scene B / Episode 1",
                "node-b1",
                new[] { b1ToB2 },
                "watch-b1",
                "scene-b");

            var b2 = new EpisodeNode(
                "ep-b2",
                "Scene B / Episode 2",
                "node-b2",
                Array.Empty<EpisodeOption>(),
                "watch-b2",
                "scene-b");

            return new ChapterDefinition(
                "debug-chapter",
                "Progression Debug Chapter",
                "ep-a1",
                Array.Empty<StatDefinition>(),
                new[] { a1, a2, b1, b2 });
        }
    }
}
