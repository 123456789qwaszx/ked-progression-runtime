using System;
using System.Collections.Generic;
using System.Linq;

namespace Ked.Progression.Save
{
    // 현재 회차의 저장 세션을 소유한다.
    // 장면 진입 시 checkpoint를 만들고, 진행 중 재개 지점을 갱신하며,
    // 정상 장면 종료 시 snapshot과 장면 기록을 확정한다.
    //
    // 호스트의 실행 상태는 직접 읽지 않는다 — 호스트가 문자열로 실어 준 값만 받는다.
    // 수동 슬롯(SaveSlotService), 갈라지기(PlaythroughForkService),
    // 파일 정리(LocalSaveMaintenance)는 여기 없다.
    public sealed class SaveCoordinator
    {
        private readonly ILocalSaveStore _localStore;
        private readonly string _contentVersion;
        private readonly Func<double> _clock;
        private readonly IProgressionLog _log;

        private PlaythroughSession _playthroughSession;

        private double _startedAt;
        private int _playSecondsBase;

        private string _playthroughId;
        private readonly List<SceneRecord> _scenes = new(); // 확정된 장면 기록(이미 끝난 장면들)
        private SceneCheckpoint _currentEntry;              // 현재 플레이 중인 장면의 "진입 당시 상태"

        // clock: 단조 증가하는 초. 플레이 시간을 잰다(예: Unity의 Time.realtimeSinceStartupAsDouble).
        public SaveCoordinator(
            ILocalSaveStore localStore,
            string contentVersion,
            Func<double> clock,
            IProgressionLog log = null)
        {
            _localStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            if (string.IsNullOrWhiteSpace(contentVersion))
                throw new ArgumentException("저장 콘텐츠 버전이 비어 있다.", nameof(contentVersion));

            _contentVersion = contentVersion;
            _log = log;
            _startedAt = _clock();
            _localStore.Initialize();
        }

        public string PlaythroughId => _playthroughId;

        // 밖에서 쓰는 저장 유스케이스가 현재 상태를 보는 유일한 창.
        // _scenes 목록과 _currentEntry 원본은 넘기지 않는다.
        public PlaythroughSaveSnapshot Capture() =>
            new(
                _playthroughId,
                new List<SceneRecord>(_scenes),
                CopyEntry(_currentEntry),
                TotalSeconds);

        private static SceneCheckpoint CopyEntry(SceneCheckpoint entry) =>
            entry == null
                ? null
                : new SceneCheckpoint
                {
                    ChapterId = entry.ChapterId,
                    EpisodeId = entry.EpisodeId,
                    Stats = new Dictionary<string, int>(entry.Stats, StringComparer.Ordinal),
                    HostSerialStart = entry.HostSerialStart,
                    PlaySecondsAtEntry = entry.PlaySecondsAtEntry,
                    EnteredAtUtc = entry.EnteredAtUtc,
                };

        private int TotalSeconds => _playSecondsBase + (int)(_clock() - _startedAt);

        #region 회차

        public void BeginNewPlaythrough()
            => BecomePlaythrough(SaveStamp.NewId(), 0, null);

        // active pointer가 가리키는 저장 파일을 읽어 이 coordinator를 그 회차에 붙인다.
        // 이어할 수 없으면(없음, 콘텐츠 버전 다름) null.
        public ResumePoint LoadActiveResumePoint()
        {
            PlaythroughSave save = _localStore.LoadActive();

            if (save == null)
                return null;

            // 버전 도입 전의 개발용 저장은 현재 콘텐츠로 한 번 승계한다.
            if (string.IsNullOrEmpty(save.ContentVersion))
                save.ContentVersion = _contentVersion;

            if (!string.Equals(save.ContentVersion, _contentVersion, StringComparison.Ordinal))
            {
                _log?.Warning(
                    $"[저장] 콘텐츠 버전이 달라 이어하지 않는다: {save.ContentVersion} → {_contentVersion}");
                return null;
            }

            string id = string.IsNullOrEmpty(save.PlaythroughId)
                ? SaveStamp.NewId()
                : save.PlaythroughId;

            BecomePlaythrough(id, save.PlaySeconds, save.Scenes);

            return new ResumePoint(
                save.ChapterId,
                save.CurrentEpisodeId,
                save.Stats,
                save.HostState,
                save.PendingLoad,
                save.ChapterCompleted);
        }

        // coordinator 전체를 특정 회차에 접속: 메모리 상태, 장면 이력, 플레이 시간, 고정 회차 session.
        private void BecomePlaythrough(string id, int playSeconds, List<SceneRecord> scenes)
        {
            _playthroughId = id;
            _playSecondsBase = playSeconds;
            _startedAt = _clock();

            _scenes.Clear();

            if (scenes != null)
                _scenes.AddRange(scenes);

            _currentEntry = null;

            _playthroughSession = _localStore.Open(id);
        }

        #endregion

        #region 장면 경계

        // 장면 진입 시점의 진행 상태와 호스트 순번 경계를 체크포인트로 확정한다.
        // 새 회차의 첫 장면이면 이때 회차 파일을 만들고 active로 세운다.
        public void EnterScene(
            string chapterId,
            ChapterState entryState,
            int hostSerialStart)
        {
            _currentEntry = new SceneCheckpoint
            {
                ChapterId = chapterId,
                EpisodeId = entryState.CurrentEpisodeId,
                Stats = entryState.Stats.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                HostSerialStart = hostSerialStart,
                PlaySecondsAtEntry = TotalSeconds,
                EnteredAtUtc = SaveStamp.NowUtc(),
            };

            if (_playthroughSession == null)
            {
                var initial = new PlaythroughSave
                {
                    PlaythroughId = _playthroughId,
                    ContentVersion = _contentVersion,
                    ChapterId = chapterId,
                    CurrentEpisodeId = entryState.CurrentEpisodeId,
                    Stats = new Dictionary<string, int>(_currentEntry.Stats),
                    SavedAtUtc = SaveStamp.NowUtc(),
                    PlaySeconds = TotalSeconds,
                };

                PlaythroughSession session =
                    _localStore.Open(_playthroughId)
                    ?? _localStore.Create(initial);

                _localStore.SetActive(_playthroughId);

                _playthroughSession = session;
            }
        }

        // 현재 장면을 확정하지 않고, 장면 진입점부터 지금 위치까지의 재생 계획만 갱신한다.
        // loadHostState: 저장 지점의 호스트 좌표와 거기까지 가는 데 필요한 호스트 기록.
        public void UpdateResumePoint(
            IReadOnlyList<CommittedChoice> path,
            string loadHostState)
        {
            if (_playthroughId == null || _playthroughSession == null)
                throw new InvalidOperationException("활성 회차 없이 이어하기 지점을 저장할 수 없다.");

            if (_currentEntry == null)
                throw new InvalidOperationException("장면 진입 보고 없이 이어하기 지점이 호출됐다.");

            if (loadHostState == null)
                throw new ArgumentNullException(nameof(loadHostState));

            PlaythroughSave snapshot = _playthroughSession.Read().Snapshot;

            snapshot.ChapterId = _currentEntry.ChapterId;
            snapshot.CurrentEpisodeId = _currentEntry.EpisodeId;
            snapshot.Stats = new Dictionary<string, int>(_currentEntry.Stats, StringComparer.Ordinal);
            snapshot.ChapterCompleted = false;
            snapshot.PendingLoad = SavedLoadPlan.Create(path, loadHostState);
            snapshot.PlaySeconds = TotalSeconds;
            snapshot.SavedAtUtc = SaveStamp.NowUtc();

            _playthroughSession.Commit(snapshot);
        }

        // 정상 장면 완료 시점의 진행 결과와 호스트 기록을 하나의 snapshot으로 확정한다.
        // 디스크 확정 성공 뒤에만 메모리 장면 기록을 교체한다.
        //
        // sceneHostState:       이 장면을 다시 달릴 때 호스트가 필요로 하는 것
        // playthroughHostState: 회차 전체에 걸쳐 이어지는 호스트 상태
        // hostSerialEnd:        다음 장면이 시작할 호스트 순번
        public void CommitScene(
            string chapterId,
            SceneCommitResult result,
            string sceneHostState,
            string playthroughHostState,
            int hostSerialEnd,
            SceneRunOutcome outcome)
        {
            if (_currentEntry == null)
                throw new InvalidOperationException("장면 진입 보고 없이 장면 확정이 호출됐다.");

            var path = new List<SavedChoice>(result.Choices.Count);

            for (int i = 0; i < result.Choices.Count; i++)
            {
                path.Add(new SavedChoice
                {
                    FromEpisodeId = result.Choices[i].FromEpisodeId,
                    OptionIndex = result.Choices[i].OptionIndex,
                });
            }

            var scenes = new List<SceneRecord>(_scenes)
            {
                new SceneRecord
                {
                    Checkpoint = _currentEntry,
                    Path = path,
                    HostState = sceneHostState ?? "",
                    HostSerialEnd = hostSerialEnd,
                },
            };

            var snapshot = new PlaythroughSave
            {
                PlaythroughId = _playthroughId,
                ContentVersion = _contentVersion,
                ChapterId = chapterId,
                CurrentEpisodeId = result.State.CurrentEpisodeId,
                Stats = result.State.Stats.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                ChapterCompleted = outcome == SceneRunOutcome.ChapterEnded,
                Scenes = scenes,
                HostState = playthroughHostState ?? "",
                PendingLoad = null,
                PlaySeconds = TotalSeconds,
                SavedAtUtc = SaveStamp.NowUtc(),
            };

            _playthroughSession.Commit(snapshot);

            _scenes.Clear();
            _scenes.AddRange(scenes);
            _currentEntry = null;
        }

        #endregion
    }
}
