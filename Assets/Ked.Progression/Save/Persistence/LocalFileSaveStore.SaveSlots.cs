using System;
using System.IO;
using System.Linq;

namespace Ked.Progression.Save
{
    public sealed partial class LocalFileSaveStore
    {
        private string SaveSlotIndexPath => Path.Combine(_directory, "save-slots.json");
        private string SaveSlotDirectory => Path.Combine(_directory, "save-slot-data");

        private string SaveSlotPath(string key)
        {
            ValidateId(key);
            return Path.Combine(SaveSlotDirectory, key + ".json");
        }

        public SaveSlotIndexFile LoadSaveSlotIndex()
        {
            SaveSlotIndexFile index =
                Read<SaveSlotIndexFile>(SaveSlotIndexPath)
                ?? new SaveSlotIndexFile { FormatVersion = SaveFormat.SaveSlotVersion };

            SaveDataValidator.ValidateSlotIndex(index);
            return index;
        }

        public SaveSlotData LoadSaveSlot(string id)
        {
            SaveSlotEntry entry = LoadSaveSlotIndex().Slots.Find(slot => slot.Id == id);
            if (entry == null) return null;

            SaveSlotFile file = Read<SaveSlotFile>(SaveSlotPath(entry.DataKey));
            SaveDataValidator.ValidateSlot(file, id);
            SaveDataValidator.ValidateSlotLink(entry, file.Data);
            return PlaythroughSession.Copy(file.Data);
        }

        // 본문을 먼저 확보하고 목록을 교체한다. 목록 쓰기가 실패하면 기존 슬롯은 그대로다.
        public void WriteSaveSlot(SaveSlotEntry entry, SaveSlotData data)
        {
            if (entry == null || data == null || entry.Id != data.Id)
                throw new ArgumentException("수동 저장의 목록과 본문 ID가 다르다.");

            SaveSlotEntry nextEntry = PlaythroughSession.Copy(entry);
            SaveSlotData nextData = PlaythroughSession.Copy(data);
            nextEntry.DataKey = Guid.NewGuid().ToString("N");

            var body = new SaveSlotFile
            {
                FormatVersion = SaveFormat.SaveSlotVersion,
                Data = nextData,
            };
            SaveDataValidator.ValidateSlot(body, nextEntry.Id);
            SaveDataValidator.ValidateSlotLink(nextEntry, nextData);

            SaveSlotIndexFile index = LoadSaveSlotIndex();
            index.Slots.RemoveAll(slot => slot.Id == nextEntry.Id);
            index.Slots.Add(nextEntry);
            SaveDataValidator.ValidateSlotIndex(index);

            Write(SaveSlotPath(nextEntry.DataKey), body);
            Write(SaveSlotIndexPath, index);
            entry.DataKey = nextEntry.DataKey;
            CollectSaveSlotFiles();
        }

        public void WriteSaveSlotIndex(SaveSlotIndexFile index)
        {
            SaveSlotIndexFile next = PlaythroughSession.Copy(index);
            SaveDataValidator.ValidateSlotIndex(next);
            Write(SaveSlotIndexPath, next);
            CollectSaveSlotFiles();
        }

        private void CollectSaveSlotFiles()
        {
            if (!Directory.Exists(SaveSlotDirectory)) return;

            var live = LoadSaveSlotIndex().Slots
                .Select(slot => slot.DataKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(SaveSlotDirectory, "*.json"))
            {
                if (live.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
