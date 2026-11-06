using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ked.Progression.Save;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 회차 하나를 다루는 자리 — 새로 시작, 이어하기, 대사 기록의 지난 줄에서 갈라지기.
    ///
    /// 갈라지기가 이 샘플의 요점이다(README 결정 E3).
    /// 1. 플레이어가 대사 기록의 줄 하나를 누른다. 그 줄 번호가 호스트 순번이다.
    /// 2. 진행 저장이 그 번호가 어느 확정 장면의 것인지 답한다(FindSceneIndexBySerial).
    /// 3. 그 장면의 루트(진입 checkpoint + 선택 경로)에서 새 회차를 연다(ForkFromScene).
    ///    장면 안 몇 번째 줄인지는 호스트 상태로 싣는다.
    /// 4. 새 회차를 이어하면 실행기가 장면 루트부터 그 줄까지 빨리 감는다.
    ///
    /// 진행 중인 장면의 줄은 갈라질 지점이 아니다 — 확정 전의 장면 안 되감기는 연출 런타임의 롤백 몫이다.
    /// </summary>
    public sealed class SamplePlaythrough
    {
        private readonly ChapterDefinition _chapter;
        private readonly SampleSceneRunner _runner;
        private readonly SampleLineLog _lines;
        private readonly SaveCoordinator _save;
        private readonly PlaythroughForkService _fork;
        private readonly IProgressionLog _log;
        private readonly ProgressionDriver _driver;

        public SamplePlaythrough(
            ChapterDefinition chapter,
            SampleSceneRunner runner,
            SampleLineLog lines,
            SaveCoordinator save,
            PlaythroughForkService fork,
            IProgressionLog log)
        {
            _chapter = chapter;
            _runner = runner;
            _lines = lines;
            _save = save;
            _fork = fork;
            _log = log;
            _driver = new ProgressionDriver(runner, log);

            // 보인 줄마다 이어하기 지점을 갱신한다. 장면 진입점 + 지금까지의 선택 경로 + 장면 안 줄 번호.
            _runner.LineShown += lineInScene =>
                _save.UpdateResumePoint(_driver.PendingPath, SampleHostState.Load(lineInScene));
        }

        public bool IsRunning => _driver.IsRunning;

        public Task StopAsync() => _driver.IsRunning ? _driver.StopAsync() : Task.CompletedTask;

        public async Task StartNewAsync()
        {
            await StopAsync();

            _save.BeginNewPlaythrough();
            _lines.Clear();
            _runner.PrepareRestore(null);

            _log?.Info($"── 새로 시작: {_chapter.DisplayName} ──");
            _driver.Start(_chapter, _chapter.CreateEntryState());
        }

        /// <summary>active 회차를 이어한다. 이어할 것이 없거나 끝난 회차면 false.</summary>
        public async Task<bool> ResumeAsync()
        {
            await StopAsync();
            return LaunchActive();
        }

        /// <summary>대사 기록의 이 줄에서 갈라질 수 있는가 — 그 줄이 확정된 장면의 것인가.</summary>
        public bool CanForkFrom(int serial) =>
            _save.PlaythroughId != null && _fork.CanForkFrom(_save.Capture(), serial);

        public async Task<bool> ForkFromLineAsync(int serial)
        {
            PlaythroughSaveSnapshot playthrough = _save.Capture();
            int sceneIndex = playthrough.FindSceneIndexBySerial(serial);

            if (sceneIndex < 0)
            {
                _log?.Info($"[샘플] {serial}번 줄은 진행 중인 장면의 것이다 — 확정 전 장면 안 되감기는 연출 런타임(롤백)의 몫이라 여기엔 없다.");
                return false;
            }

            await StopAsync();

            int sceneStart = playthrough.Scenes[sceneIndex].Checkpoint.HostSerialStart;

            _fork.ForkFromScene(
                playthrough,
                sceneIndex,
                inheritedHostState: SampleHostState.Playthrough(_lines.Before(sceneStart)),
                loadHostState: SampleHostState.Load(serial - sceneStart));

            return LaunchActive();
        }

        private bool LaunchActive()
        {
            ResumePoint resume = _save.LoadActiveResumePoint();

            if (resume == null)
            {
                _log?.Info("[샘플] 이어할 회차가 없다. [처음부터]로 시작한다.");
                return false;
            }

            List<SampleLine> lines;
            int? seekLine;

            try
            {
                lines = SampleHostState.ReadPlaythrough(resume.HostState);
                seekLine = resume.LoadPlan == null ? null : SampleHostState.ReadLoad(resume.LoadPlan.HostState);
            }
            catch (InvalidDataException error)
            {
                _log?.Warning($"[샘플] 저장된 호스트 상태를 읽지 못했다 — {error.Message}");
                return false;
            }

            _lines.Restore(lines);

            if (resume.ChapterCompleted)
            {
                _log?.Info("[샘플] 끝난 회차다. 대사 기록에서 지난 장면의 줄을 눌러 거기서부터 다시 할 수 있다.");
                return false;
            }

            if (resume.ChapterId != _chapter.ChapterId || !_chapter.TryGetNode(resume.EpisodeId, out _))
            {
                _log?.Warning($"[샘플] 저장 지점 {resume.ChapterId}/{resume.EpisodeId}가 지금 챕터에 없다.");
                return false;
            }

            ChapterState state = ChapterState.Restore(_chapter, resume.EpisodeId, resume.Stats);

            IReadOnlyList<ScenePathStep> path = resume.LoadPlan?.Path
                .Select(step => new ScenePathStep(step.FromEpisodeId, step.OptionIndex))
                .ToList();

            _runner.PrepareRestore(seekLine);

            _log?.Info(resume.LoadPlan == null
                ? $"── 이어하기: {resume.EpisodeId}의 장면 루트에서 ──"
                : $"── 이어하기: {resume.EpisodeId}의 장면, 선택 {path.Count}개를 다시 쓰고 장면 안 {seekLine}번 줄까지 ──");

            _driver.Start(_chapter, state, path);
            return true;
        }
    }
}
