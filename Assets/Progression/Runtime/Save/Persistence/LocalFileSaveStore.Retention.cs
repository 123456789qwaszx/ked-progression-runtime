using System;
using System.IO;

namespace Ked.Progression.Save
{
    public sealed partial class LocalFileSaveStore
    {
        // 자동 저장은 active 한 건만 필요하다. 수동 슬롯은 독립 본문을 가지므로 원본 회차를 붙잡지 않는다.
        public int CollectUnusedPlaythroughs()
        {
            string active = ActiveId;
            int removed = 0;

            foreach (string id in ListPlaythroughIds())
            {
                if (string.Equals(id, active, StringComparison.Ordinal)) continue;

                PlaythroughSession session = Open(id);
                File.Delete(PathOf(id));
                session.Close();
                _sessions.Remove(id);
                removed++;
            }

            CollectSaveSlotFiles();
            return removed;
        }
    }
}
