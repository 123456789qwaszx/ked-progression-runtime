using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ked.Progression.Save;
using Newtonsoft.Json;
using UnityEngine;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 진행 코어 샘플의 조립 지점. Play하면 스스로 선다 — 씬에 놓을 것이 없다.
    ///
    /// 조립 순서
    /// 1. 챕터 JSON(Resources/ProgressionSample/sample_chapter) → ChapterProgressionDto → ProgressionLoader.Load
    /// 2. 대본(Resources/ProgressionSample/sample_script) → SampleScript
    /// 3. 진행 저장 — LocalFileSaveStore(persistentDataPath/ked-progression-sample) + SaveCoordinator + 갈라지기
    /// 4. 화면(ProgressionSampleView) + 대사 기록(SampleLineLog) + 장면 실행기(SampleSceneRunner)
    /// 5. 회차(SamplePlaythrough) — [처음부터] / [이어하기] / 대사 기록의 줄 눌러 갈라지기
    ///
    /// 진행 코어는 장면 실행 순서를 갖지 않는다. 호스트가 ISceneRunner로 준다.
    /// 진행 저장은 엔진을 모른다. 시계와 로그를 여기서 꽂는다.
    /// </summary>
    public sealed class ProgressionSampleBootstrap : MonoBehaviour, IProgressionLog
    {
        private const string ChapterResource = "ProgressionSample/sample_chapter";
        private const string ScriptResource = "ProgressionSample/sample_script";
        private const string SaveFolder = "ked-progression-sample";
        private const string ContentVersion = "sample-v1";

        private ProgressionSampleView _view;
        private SamplePlaythrough _playthrough;
        private LocalSaveMaintenance _maintenance;

        private bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode)
                return;

            if (FindFirstObjectByType<ProgressionSampleBootstrap>() != null)
                return;

            GameObject root = new("Progression Sample");
            DontDestroyOnLoad(root);
            root.AddComponent<ProgressionSampleBootstrap>();
        }

        private void Awake()
        {
            _view = ProgressionSampleView.Create(transform);

            ChapterDefinition chapter = LoadChapter();
            SampleScript script = SampleScript.Parse(LoadText(ScriptResource));
            var lines = new SampleLineLog();

            if (chapter == null)
            {
                _view.Bind(null, lines, null);
                return;
            }

            string folder = Path.Combine(Application.persistentDataPath, SaveFolder);
            var store = new LocalFileSaveStore(folder);
            var save = new SaveCoordinator(store, ContentVersion, () => Time.realtimeSinceStartupAsDouble, this);
            var fork = new PlaythroughForkService(store, ContentVersion, this);
            _maintenance = new LocalSaveMaintenance(store, this);

            var runner = new SampleSceneRunner(
                _view,
                _view,
                script,
                lines,
                new SampleSaveBridge(save, lines),
                this);

            _playthrough = new SamplePlaythrough(chapter, runner, lines, save, fork, this);

            _view.Bind(chapter, lines, _playthrough.CanForkFrom);
            _view.RestartClicked += HandleRestart;
            _view.ContinueClicked += HandleContinue;
            _view.LineClicked += HandleLineClicked;

            Info($"[샘플] 저장 위치: {folder}");
        }

        private void Update()
        {
            _maintenance?.Tick(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleRestart() => Run(() => _playthrough.StartNewAsync());

        private void HandleContinue() => Run(() => _playthrough.ResumeAsync());

        private void HandleLineClicked(int serial) => Run(() => _playthrough.ForkFromLineAsync(serial));

        // 버튼이 겹쳐 눌려도 한 번에 하나만.
        private async void Run(Func<Task> action)
        {
            if (_busy)
                return;

            _busy = true;

            try
            {
                await action();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
            finally
            {
                _busy = false;
            }
        }

        private ChapterDefinition LoadChapter()
        {
            string json = LoadText(ChapterResource);

            if (json == null)
                return null;

            ChapterProgressionDto dto;

            try
            {
                dto = JsonConvert.DeserializeObject<ChapterProgressionDto>(json);
            }
            catch (JsonException error)
            {
                Debug.LogError($"[샘플] 챕터 JSON 역직렬화 실패: {error.Message}", this);
                return null;
            }

            if (dto == null)
            {
                Debug.LogError("[샘플] 챕터 JSON이 비어 있다.", this);
                return null;
            }

            ProgressionLoadResult result = ProgressionLoader.Load(dto);

            LogDiagnostics(result.Diagnostics);

            return result.IsValid ? result.Chapter : null;
        }

        private string LoadText(string resource)
        {
            TextAsset asset = Resources.Load<TextAsset>(resource);

            if (asset != null)
                return asset.text;

            Debug.LogError($"[샘플] Resources/{resource} 를 찾지 못했다.", this);
            return null;
        }

        private void LogDiagnostics(IReadOnlyList<ProgressionDiagnostic> diagnostics)
        {
            foreach (ProgressionDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == ProgressionDiagnosticSeverity.Error)
                    Debug.LogError($"[샘플] 챕터 진단: {diagnostic}", this);
                else
                    Debug.LogWarning($"[샘플] 챕터 진단: {diagnostic}", this);
            }
        }

        #region IProgressionLog — 드라이버·저장의 기록을 콘솔과 화면에 함께 남긴다

        public void Info(string message)
        {
            Debug.Log(message, this);
            _view.AppendLog(message);
        }

        public void Warning(string message)
        {
            Debug.LogWarning(message, this);
            _view.AppendLog(message);
        }

        public void Error(string message)
        {
            Debug.LogError(message, this);
            _view.AppendLog(message);
        }

        #endregion

        private void OnDestroy()
        {
            if (_view != null)
            {
                _view.RestartClicked -= HandleRestart;
                _view.ContinueClicked -= HandleContinue;
                _view.LineClicked -= HandleLineClicked;
            }

            if (_playthrough != null && _playthrough.IsRunning)
                _ = _playthrough.StopAsync();
        }
    }
}
