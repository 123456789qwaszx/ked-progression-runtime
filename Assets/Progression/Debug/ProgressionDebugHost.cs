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

        private ProgressionState _savedState;
        private int? _rollbackTarget;

        private string _currentChapterId = "-";
        private string _currentSceneId = "-";
        private string _currentEpisodeId = "-";
        private string _currentNode = "-";

        public bool IsSeekingActive { get; private set; }
        public int LastHistoryIndex { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode)
                return;

            if (FindFirstObjectByType<ProgressionDebugHost>() != null)
                return;

            var host = new GameObject("Progression Debug Host");
            DontDestroyOnLoad(host);
            host.AddComponent<ProgressionDebugHost>();
        }

        private void Awake()
        {
            _chapter = CreateDebugChapter();

            // Continue/Manual Load를 처음부터 시험할 수 있도록 Scene B root의
            // committed checkpoint 하나를 가짜 저장 상태로 준비한다.
            _savedState = ProgressionState.Restore(
                _chapter,
                "ep-b1",
                new Dictionary<string, int>());

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
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 390, 680), GUI.skin.box);

            GUILayout.Label("Progression Runtime Debug");
            GUILayout.Space(6);

            GUILayout.Label("Session / Host lifecycle");
            if (GUILayout.Button("New Game"))
                Run(NewGameAsync);

            if (GUILayout.Button("Continue"))
                Run(ContinueAsync);

            if (GUILayout.Button("Manual Load"))
                Run(ManualLoadAsync);

            if (GUILayout.Button("Stop / Title Exit"))
                Run(StopAsync);

            GUILayout.Space(8);
            GUILayout.Label("Current playback");

            if (GUILayout.Button("Complete Episode Node"))
                CompleteCurrentNode("normal complete");

            if (GUILayout.Button("Episode Skip"))
            {
                Info("[PRESENT] EPISODE SKIP REQUEST — progression cursor를 직접 바꾸지 않는다.");
                CompleteCurrentNode("episode skip");
            }

            DrawChoiceButtons();

            GUILayout.Space(8);
            GUILayout.Label("Scene replay");

            if (GUILayout.Button("Rollback 1 Step"))
                Run(() => ReplayAsync(Math.Max(0, LastHistoryIndex - 1), "Rollback"));

            if (GUILayout.Button("Backlog Jump 2 Steps"))
                Run(() => ReplayAsync(Math.Max(0, LastHistoryIndex - 2), "Backlog Jump"));

            GUILayout.Space(10);
            GUILayout.Label("Current");
            GUILayout.Label($"Running  : {_driver?.IsRunning == true}");
            GUILayout.Label($"Chapter  : {_currentChapterId}");
            GUILayout.Label($"Scene    : {_currentSceneId}");
            GUILayout.Label($"Episode  : {_currentEpisodeId}");
            GUILayout.Label($"Node     : {_currentNode}");
            GUILayout.Label($"History  : {LastHistoryIndex}");
            GUILayout.Label($"Pending  : {_driver?.PendingPath.Count ?? 0}");
            GUILayout.Label($"Seeking  : {IsSeekingActive}");
            GUILayout.Label($"Saved Ep : {_savedState?.CurrentEpisodeId ?? "-"}");

            GUILayout.Space(8);
            GUILayout.Label("Console tags");
            GUILayout.Label("[LIFE] Chapter / Scene / Episode");
            GUILayout.Label("[RUN]  start / cancel / stop");
            GUILayout.Label("[REPLAY] rollback / backlog jump");
            GUILayout.Label("[PRESENT] skip / playback");
            GUILayout.Label("[STATE] commit result");

            GUILayout.EndArea();
        }

        private void DrawChoiceButtons()
        {
            if (_choiceGate == null || _choiceGate.Task.IsCompleted)
                return;

            GUILayout.Space(6);
            GUILayout.Label("Choice");

            for (int i = 0; i < _currentOptions.Count; i++)
            {
                ResolvedOption option = _currentOptions[i];
                bool before = GUI.enabled;
                GUI.enabled = option.IsSelectable;

                string label = string.IsNullOrEmpty(option.Option.ChoiceLabel)
                    ? $"Choice {i}"
                    : $"{i}: {option.Option.ChoiceLabel}";

                if (GUILayout.Button(label))
                    _choiceGate.TrySetResult(i);

                GUI.enabled = before;
            }
        }

        private async Task NewGameAsync()
        {
            Info("[HOST] NEW GAME REQUEST");
            await StopCurrentRunAsync();
            ResetTransientPlaybackState();

            _driver.Start(
                _chapter,
                _chapter.CreateEntryState());
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
            _driver.Start(_chapter, _savedState);
            return Task.CompletedTask;
        }

        private async Task ManualLoadAsync()
        {
            Info("[HOST] MANUAL LOAD REQUEST");
            await StopCurrentRunAsync();
            ResetTransientPlaybackState();

            _driver.Start(_chapter, _savedState);
        }

        private async Task StopAsync()
        {
            Info("[HOST] TITLE EXIT / STOP REQUEST");
            await StopCurrentRunAsync();
        }

        private async Task StopCurrentRunAsync()
        {
            if (!_driver.IsRunning)
                return;

            await _driver.StopAsync();
            Info("[HOST] previous run discarded");
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
            _nodeGate = null;
            _choiceGate = null;
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
            _choiceGate = new TaskCompletionSource<int>();

            Info($"[PRESENT] CHOICE OPEN visible={options.Count} hidden={hiddenCount}");

            try
            {
                return await _choiceGate.Task;
            }
            finally
            {
                _currentOptions = Array.Empty<ResolvedOption>();
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
            Info("[REPLAY] LOAD REPLAY BEGIN");
        }

        public void ClearSeek()
        {
            IsSeekingActive = false;
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
            Info($"[LIFE][CHAPTER] ENTER chapter={chapterId} episode={state.CurrentEpisodeId}");
        }

        public void ReportChapterExited(string chapterId, ProgressionState state)
        {
            Info($"[LIFE][CHAPTER] EXIT chapter={chapterId} episode={state.CurrentEpisodeId}");
        }

        public void ReportSceneEntered(
            string chapterId,
            string sceneId,
            ProgressionState entryState)
        {
            _currentSceneId = sceneId;
            Info($"[LIFE][SCENE] ENTER scene={sceneId} root={entryState.CurrentEpisodeId}");
        }

        public void ReportSceneCommitted(
            string chapterId,
            string sceneId,
            IReadOnlyList<CommittedChoice> choices,
            IReadOnlyList<string> watchedEpisodeIds,
            ProgressionState state)
        {
            _savedState = state;

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
            Info($"[LIFE][SCENE] EXIT scene={sceneId}");
        }

        public void ReportEpisodeEntered(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
            _currentEpisodeId = episode.EpisodeId;
            Info($"[LIFE][EPISODE] ENTER episode={episode.EpisodeId} scene={sceneId}");
        }

        public void ReportEpisodeExited(
            string chapterId,
            string sceneId,
            EpisodeNode episode)
        {
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
