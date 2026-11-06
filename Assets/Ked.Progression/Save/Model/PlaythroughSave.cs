using System.Collections.Generic;

namespace Ked.Progression.Save
{
    // 회차(playthrough) 하나의 확정 상태. 자동 저장 한 건이다.
    //
    // 진행 겹만 안다 — 챕터, 에피소드, 스탯, 장면 경로, 완료 장면 기록.
    // 호스트(연출)가 같은 시점에 보관해야 하는 것은 HostState 문자열로 싣는다.
    // 그 안이 무엇인지(백로그, 줄 위치, 선택 기록…)는 호스트만 안다.
    public sealed class PlaythroughSave
    {
        public string PlaythroughId;
        public string ContentVersion;
        public string ChapterId;
        public string CurrentEpisodeId;
        public Dictionary<string, int> Stats = new();
        public bool ChapterCompleted;
        public List<SceneRecord> Scenes = new();

        // 회차 전체에 걸쳐 이어지는 호스트 상태. 장면 확정 때마다 통째로 갱신된다.
        public string HostState = "";

        public SavedLoadPlan PendingLoad;
        public int PlaySeconds;
        public string SavedAtUtc;
    }

    // 장면 진입 당시 상태. 완료된 장면으로 돌아갈 때 유일한 시작점이다.
    public sealed class SceneCheckpoint
    {
        public string ChapterId;
        public string EpisodeId;
        public Dictionary<string, int> Stats = new();

        // 호스트가 세는 단조 증가 순번의 장면 시작 경계.
        // 호스트가 자기 순번(예: 대사 줄 번호)으로 "어느 장면의 것인가"를 되묻는 근거다.
        public int HostSerialStart;

        public int PlaySecondsAtEntry;
        public string EnteredAtUtc;
    }

    // 완료된 장면 하나: 진입 상태와 그 장면 안에서 확정된 선택 기록.
    public sealed class SceneRecord
    {
        public SceneCheckpoint Checkpoint;
        public List<SavedChoice> Path = new();

        // 이 장면을 다시 달릴 때 호스트가 필요로 하는 것.
        public string HostState = "";

        public int HostSerialEnd;
    }

    public sealed class SavedChoice
    {
        public string FromEpisodeId;
        public int OptionIndex;
    }

    // 장면 루트에서 저장 지점까지 재생하기 위한 일회성 계획.
    public sealed class SavedLoadPlan
    {
        public List<SavedChoice> Path = new();

        // 저장 지점의 호스트 좌표와, 거기까지 가는 데 호스트가 필요로 하는 것.
        public string HostState = "";

        // 지금 진행 중인 장면의 좌표를 재생 계획으로 옮긴다.
        // 이어하기 지점과 수동 슬롯이 같은 계획을 쓴다.
        public static SavedLoadPlan Create(
            IReadOnlyList<CommittedChoice> path,
            string hostState)
        {
            var savedPath = new List<SavedChoice>(path.Count);

            for (int i = 0; i < path.Count; i++)
            {
                savedPath.Add(new SavedChoice
                {
                    FromEpisodeId = path[i].FromEpisodeId,
                    OptionIndex = path[i].OptionIndex,
                });
            }

            return new SavedLoadPlan
            {
                Path = savedPath,
                HostState = hostState ?? "",
            };
        }
    }

    // 회차 파일 envelope. FormatVersion은 저장 JSON의 모양만 판별한다.
    public sealed class PlaythroughFile
    {
        public int FormatVersion;
        public PlaythroughSave Snapshot;
    }

    // 슬롯 목록 파일에 들어가는 가벼운 표시 정보.
    public sealed class SaveSlotIndexFile
    {
        public int FormatVersion;
        public List<SaveSlotEntry> Slots = new();
    }

    public sealed class SaveSlotEntry
    {
        public string Id;
        public string DataKey;
        public string Label;
        public string Preview;
        public string ChapterId;
        public string SavedAtUtc;
        public int PlaySeconds;
    }

    // 슬롯 본문 파일의 envelope.
    public sealed class SaveSlotFile
    {
        public int FormatVersion;
        public SaveSlotData Data;
    }

    // 슬롯 하나를 원래 회차 파일 없이도 복원할 수 있는 독립 데이터.
    public sealed class SaveSlotData
    {
        public string Id;
        public string ContentVersion;
        public SceneCheckpoint Checkpoint;
        public SavedLoadPlan LoadPlan;
        public List<SceneRecord> Scenes = new();
        public string HostState = "";
        public int PlaySeconds;
    }

    public static class SaveFormat
    {
        // 4: 호스트 상태가 문자열 한 칸으로 들어왔다(백로그·줄 좌표·선택 기록이 진행 겹에서 빠짐).
        public const int PlaythroughVersion = 4;
        public const int SaveSlotVersion = 2;
    }
}
