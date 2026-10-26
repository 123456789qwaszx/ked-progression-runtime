using System.Collections.Generic;

namespace Ked.Progression.Save
{
    // SaveCoordinator가 지금 들고 있는 저장 상태의 읽기 경계.
    //
    // 수동 슬롯과 갈라지기는 이 너머를 보지 않는다.
    // 새 도메인 계층이 아니라, private 필드를 밖으로 넘기지 않기 위한 사본이다.
    //
    // 회차 전체의 호스트 상태는 여기에 없다. 회차 파일이 들고 있으므로
    // 필요한 쪽이 ILocalSaveStore에서 직접 읽는다.
    public sealed class PlaythroughSaveSnapshot
    {
        public string PlaythroughId { get; }

        // 이미 확정된 장면 기록. 목록은 사본이고, 항목은 읽기 전용으로만 쓴다.
        public IReadOnlyList<SceneRecord> Scenes { get; }

        // 진행 중인 장면의 진입 상태. 실행 중이 아니면 null.
        public SceneCheckpoint CurrentEntry { get; }

        public int PlaySeconds { get; }

        public PlaythroughSaveSnapshot(
            string playthroughId,
            IReadOnlyList<SceneRecord> scenes,
            SceneCheckpoint currentEntry,
            int playSeconds)
        {
            PlaythroughId = playthroughId;
            Scenes = scenes;
            CurrentEntry = currentEntry;
            PlaySeconds = playSeconds;
        }

        // 호스트 순번이 속한 완료 장면의 번호. 아직 확정되지 않은(현재) 장면이면 -1.
        public int FindSceneIndexBySerial(int hostSerial)
        {
            for (int i = 0; i < Scenes.Count; i++)
            {
                SceneRecord scene = Scenes[i];

                if (hostSerial >= scene.Checkpoint.HostSerialStart
                    && hostSerial < scene.HostSerialEnd)
                    return i;
            }

            return -1;
        }
    }
}
