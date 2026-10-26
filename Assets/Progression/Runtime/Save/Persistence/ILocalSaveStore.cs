using System.Collections.Generic;

namespace Ked.Progression.Save
{
    public interface ILocalSaveStore
    {
        void Initialize();
        PlaythroughSession Open(string id);
        PlaythroughSession Create(PlaythroughSave save);
        void SetActive(string id);
        PlaythroughSave LoadActive();
        string ActiveId { get; }
        PlaythroughSave LoadPlaythrough(string id);
        IReadOnlyList<string> ListPlaythroughIds();

        SaveSlotIndexFile LoadSaveSlotIndex();
        SaveSlotData LoadSaveSlot(string id);
        void WriteSaveSlot(SaveSlotEntry entry, SaveSlotData data);
        void WriteSaveSlotIndex(SaveSlotIndexFile index);

        int CollectUnusedPlaythroughs();
    }
}
