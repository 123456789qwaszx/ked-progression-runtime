using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Ked.Progression.Save
{
    // 읽기 경계에서 저장 데이터의 모양을 검사한다. 호스트 상태 문자열의 안은 보지 않는다.
    public static class SaveDataValidator
    {
        public static void ValidatePlaythrough(
            PlaythroughFile file,
            string expectedId,
            bool allowMissingContentVersion = false)
        {
            if (file == null || file.FormatVersion != SaveFormat.PlaythroughVersion)
                throw Invalid("지원하지 않는 회차 파일 형식이다.");

            ValidateSnapshot(file.Snapshot, expectedId, allowMissingContentVersion);
        }

        public static void ValidateSlotIndex(SaveSlotIndexFile file)
        {
            if (file == null || file.FormatVersion != SaveFormat.SaveSlotVersion || file.Slots == null)
                throw Invalid("지원하지 않는 수동 저장 목록 형식이다.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (SaveSlotEntry entry in file.Slots)
            {
                if (entry == null || !ValidId(entry.Id) || !ValidId(entry.DataKey)
                    || !ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.ChapterId)
                    || entry.PlaySeconds < 0 || !ValidUtc(entry.SavedAtUtc))
                    throw Invalid("수동 저장 목록에 잘못된 항목이 있다.");
            }
        }

        public static void ValidateSlot(
            SaveSlotFile file,
            string expectedId,
            bool allowMissingContentVersion = false)
        {
            if (file == null || file.FormatVersion != SaveFormat.SaveSlotVersion || file.Data == null)
                throw Invalid("지원하지 않는 수동 저장 본문 형식이다.");

            SaveSlotData data = file.Data;
            if (data.Id != expectedId
                || (!allowMissingContentVersion && string.IsNullOrWhiteSpace(data.ContentVersion))
                || data.PlaySeconds < 0 || data.Scenes == null || data.HostState == null)
                throw Invalid("수동 저장 본문이 손상됐다.");

            ValidateCheckpoint(data.Checkpoint);
            ValidateLoadPlan(data.LoadPlan, required: true);
            ValidateScenes(data.Scenes);
        }

        public static void ValidateSlotLink(SaveSlotEntry entry, SaveSlotData data)
        {
            if (entry == null || data == null || entry.Id != data.Id
                || entry.ChapterId != data.Checkpoint.ChapterId
                || entry.PlaySeconds != data.PlaySeconds)
                throw Invalid("수동 저장 목록과 본문이 서로 맞지 않는다.");
        }

        private static void ValidateSnapshot(
            PlaythroughSave save,
            string expectedId,
            bool allowMissingContentVersion)
        {
            if (save == null || save.PlaythroughId != expectedId || !ValidId(save.PlaythroughId)
                || (!allowMissingContentVersion && string.IsNullOrWhiteSpace(save.ContentVersion))
                || string.IsNullOrWhiteSpace(save.ChapterId)
                || (!save.ChapterCompleted && string.IsNullOrWhiteSpace(save.CurrentEpisodeId))
                || save.Stats == null || save.Scenes == null || save.HostState == null
                || save.PlaySeconds < 0 || !ValidUtc(save.SavedAtUtc))
                throw Invalid("회차 snapshot이 손상됐다.");

            ValidateScenes(save.Scenes);
            ValidateLoadPlan(save.PendingLoad, required: false);
        }

        private static void ValidateScenes(IReadOnlyList<SceneRecord> scenes)
        {
            foreach (SceneRecord scene in scenes)
            {
                if (scene == null || scene.Path == null || scene.HostState == null)
                    throw Invalid("완료 장면 기록이 손상됐다.");

                ValidateCheckpoint(scene.Checkpoint);
                if (scene.HostSerialEnd < scene.Checkpoint.HostSerialStart)
                    throw Invalid("완료 장면의 호스트 순번 범위가 잘못됐다.");

                ValidatePath(scene.Path, "완료 장면의 선택 기록이 잘못됐다.");
            }
        }

        private static void ValidateCheckpoint(SceneCheckpoint checkpoint)
        {
            if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.ChapterId)
                || string.IsNullOrWhiteSpace(checkpoint.EpisodeId) || checkpoint.Stats == null
                || checkpoint.HostSerialStart < 0 || checkpoint.PlaySecondsAtEntry < 0
                || !ValidUtc(checkpoint.EnteredAtUtc))
                throw Invalid("장면 체크포인트가 손상됐다.");
        }

        private static void ValidateLoadPlan(SavedLoadPlan plan, bool required)
        {
            if (plan == null)
            {
                if (required) throw Invalid("수동 저장의 재생 계획이 없다.");
                return;
            }

            if (plan.Path == null || plan.HostState == null)
                throw Invalid("저장 위치의 재생 계획이 잘못됐다.");

            ValidatePath(plan.Path, "저장 위치의 선택 기록이 잘못됐다.");
        }

        private static void ValidatePath(IReadOnlyList<SavedChoice> path, string message)
        {
            foreach (SavedChoice choice in path)
                if (choice == null || string.IsNullOrWhiteSpace(choice.FromEpisodeId) || choice.OptionIndex < 0)
                    throw Invalid(message);
        }

        private static bool ValidId(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            return true;
        }

        private static bool ValidUtc(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);

        private static InvalidDataException Invalid(string message) => new(message);
    }
}
