using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// 3. 화면(ProgressionSampleView) + 장면 실행기(SampleSceneRunner) + ProgressionDriver
    /// 4. [처음부터] → driver.Start(chapter, chapter.CreateEntryState())
    ///
    /// 진행 코어는 장면 실행 순서를 갖지 않는다. 호스트가 ISceneRunner로 준다.
    /// 이 샘플의 실행기는 최소 구현이고, 리플레이·저장까지 갖춘 구현은
    /// ked-presentation-runtime의 ProgressionHost/SceneRunner다.
    /// </summary>
    public sealed class ProgressionSampleBootstrap : MonoBehaviour, IProgressionLog
    {
        private const string ChapterResource = "ProgressionSample/sample_chapter";
        private const string ScriptResource = "ProgressionSample/sample_script";

        private ProgressionSampleView _view;
        private ChapterDefinition _chapter;
        private ProgressionDriver _driver;

        private bool _restarting;

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
            _view.RestartClicked += HandleRestartClicked;

            _chapter = LoadChapter();
            SampleScript script = SampleScript.Parse(LoadText(ScriptResource));

            _view.Bind(script, _chapter);

            if (_chapter == null)
                return;

            SampleSceneRunner runner = new(_view, _view);
            _driver = new ProgressionDriver(runner, this);
        }

        private async void HandleRestartClicked()
        {
            if (_driver == null || _restarting)
                return;

            _restarting = true;

            try
            {
                if (_driver.IsRunning)
                    await _driver.StopAsync();

                _view.AppendLog($"── 새로 시작: {_chapter.DisplayName} ──");
                _driver.Start(_chapter, _chapter.CreateEntryState());
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
            finally
            {
                _restarting = false;
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

        #region IProgressionLog — 드라이버의 기록을 콘솔과 화면에 함께 남긴다

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
                _view.RestartClicked -= HandleRestartClicked;

            if (_driver != null && _driver.IsRunning)
                _ = _driver.StopAsync();
        }
    }
}
