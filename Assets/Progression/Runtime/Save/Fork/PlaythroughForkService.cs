using System;
using System.Collections.Generic;
using System.Linq;

namespace Ked.Progression.Save
{
    #region 용어

    // SceneCheckpoint   = 재시작할 상태
    // SceneRecord       = 과거 한 장면의 완성된 기록
    // SavedLoadPlan     = 장면 루트에서 저장 지점까지 재생할 방법
    //
    // 갈라지기 = 확정된 과거 지점에서 새 회차 파일을 만들고 active로 세우는 것.
    // 여기서 끝이다. 만들어진 회차에 SaveCoordinator를 붙이는 일은
    // 곧이어 호출되는 LoadActiveResumePoint()가 한다.
    //
    // "백로그 몇 번째 줄"처럼 호스트 순번으로 지점을 고르는 일은 둘로 나뉜다.
    //   어느 장면인가       → PlaythroughSaveSnapshot.FindSceneIndexBySerial (진행 겹)
    //   그 장면 안 어디인가 → 호스트가 자기 기록으로 loadHostState를 만든다 (호스트 겹)

    #endregion

    public sealed class PlaythroughForkService
    {
        private readonly ILocalSaveStore _store;
        private readonly string _contentVersion;
        private readonly IProgressionLog _log;

        public PlaythroughForkService(
            ILocalSaveStore store,
            string contentVersion,
            IProgressionLog log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            if (string.IsNullOrWhiteSpace(contentVersion))
                throw new ArgumentException("저장 콘텐츠 버전이 비어 있다.", nameof(contentVersion));

            _contentVersion = contentVersion;
            _log = log;
        }

        // 호스트 순번이 이미 확정된 장면의 것이면 그 지점에서 갈라질 수 있다.
        public bool CanForkFrom(PlaythroughSaveSnapshot playthrough, int hostSerial)
            => playthrough.FindSceneIndexBySerial(hostSerial) >= 0;

        // 과거 장면 하나를 출발점으로 새 회차를 만든다.
        //
        // 1. 해당 장면 진입 checkpoint 복원
        // 2. 그 장면 이전 기록만 상속
        // 3. loadHostState가 있으면 저장된 선택 경로를 따라 그 지점까지 재생,
        //    null이면 장면 루트부터 일반 플레이
        // 4. 새 회차 파일로 저장하고 active로 세움
        //
        // inheritedHostState: 그 장면이 시작되기 전까지의 회차 호스트 상태(호스트가 잘라 준다).
        public void ForkFromScene(
            PlaythroughSaveSnapshot playthrough,
            int sceneIndex,
            string inheritedHostState,
            string loadHostState)
        {
            if (sceneIndex < 0 || sceneIndex >= playthrough.Scenes.Count)
                throw new ArgumentOutOfRangeException(nameof(sceneIndex));

            SceneRecord origin = playthrough.Scenes[sceneIndex];
            SceneCheckpoint checkpoint = origin.Checkpoint;

            string fromId = playthrough.PlaythroughId;
            string newId = SaveStamp.NewId();

            var file = new PlaythroughSave
            {
                PlaythroughId = newId,
                ContentVersion = _contentVersion,
                ChapterId = checkpoint.ChapterId,
                CurrentEpisodeId = checkpoint.EpisodeId,
                Stats = new Dictionary<string, int>(checkpoint.Stats, StringComparer.Ordinal),
                ChapterCompleted = false,

                // 갈라질 장면은 새 회차에서 다시 실행하므로 그 이전 장면까지만 물려받는다.
                Scenes = playthrough.Scenes.Take(sceneIndex).ToList(),
                HostState = inheritedHostState ?? "",

                PendingLoad = loadHostState == null
                    ? null
                    : new SavedLoadPlan
                    {
                        Path = origin.Path
                            .Select(choice => new SavedChoice
                            {
                                FromEpisodeId = choice.FromEpisodeId,
                                OptionIndex = choice.OptionIndex,
                            })
                            .ToList(),
                        HostState = loadHostState,
                    },

                PlaySeconds = checkpoint.PlaySecondsAtEntry,
                SavedAtUtc = SaveStamp.NowUtc(),
            };

            SaveAndActivate(file);

            _log?.Info(
                $"[저장] 갈라지기 — {fromId} 장면 {sceneIndex}({checkpoint.EpisodeId}) → 새 회차 {newId}. " +
                $"물려받은 기록 {file.Scenes.Count}개, 시간 {checkpoint.PlaySecondsAtEntry}s" +
                (loadHostState == null ? " — 장면 루트에서" : " — 저장 지점까지 달린다"));
        }

        // 수동 슬롯 본문을 독립된 새 회차로 연다. 슬롯 원본은 바꾸지 않는다.
        public void ForkFromSaveSlot(SaveSlotEntry entry, SaveSlotData data)
        {
            SceneCheckpoint checkpoint = data.Checkpoint;

            List<SceneRecord> inheritedScenes = PlaythroughSession.Copy(data.Scenes);

            string newId = SaveStamp.NewId();

            var file = new PlaythroughSave
            {
                PlaythroughId = newId,
                ContentVersion = _contentVersion,
                ChapterId = checkpoint.ChapterId,
                CurrentEpisodeId = checkpoint.EpisodeId,
                Stats = new Dictionary<string, int>(checkpoint.Stats, StringComparer.Ordinal),
                ChapterCompleted = false,
                Scenes = inheritedScenes,
                HostState = data.HostState ?? "",
                PendingLoad = PlaythroughSession.Copy(data.LoadPlan),
                PlaySeconds = data.PlaySeconds,
                SavedAtUtc = SaveStamp.NowUtc(),
            };

            SaveAndActivate(file);

            _log?.Info(
                $"[저장] 수동 슬롯 불러오기 — \"{entry.Preview}\" → 새 회차 {newId}. " +
                $"물려받은 기록 {inheritedScenes.Count}개, 시간 {data.PlaySeconds}s");
        }

        private void SaveAndActivate(PlaythroughSave file)
        {
            _store.Create(file);
            _store.SetActive(file.PlaythroughId);
        }
    }
}
