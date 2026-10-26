using System;
using System.Collections.Generic;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 샘플의 대본. DialogueEntryId마다 보여 줄 줄을 갖는다.
    ///
    /// 형식 — '# 키' 한 줄 뒤에 그 키의 대사가 이어진다. 빈 줄은 건너뛴다.
    /// <code>
    /// # gate
    /// 낡은 석문 앞에 섰다.
    /// 문은 반쯤 열려 있다.
    /// </code>
    ///
    /// 실제 게임에서는 이 자리를 Yarn 같은 대본 런타임이 맡는다.
    /// 진행 코어는 DialogueEntryId라는 키만 넘기고, 그 안을 모른다.
    /// </summary>
    public sealed class SampleScript
    {
        private readonly Dictionary<string, List<string>> _lines;

        private SampleScript(Dictionary<string, List<string>> lines)
        {
            _lines = lines;
        }

        public static SampleScript Parse(string text)
        {
            var lines = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> current = null;

            string[] rows = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            foreach (string raw in rows)
            {
                string row = raw.Trim();

                if (row.Length == 0)
                    continue;

                if (row.StartsWith("#", StringComparison.Ordinal))
                {
                    string key = row.Substring(1).Trim();

                    if (!lines.TryGetValue(key, out current))
                    {
                        current = new List<string>();
                        lines.Add(key, current);
                    }

                    continue;
                }

                // 키 앞의 줄은 버린다 — 어느 에피소드의 대사인지 모른다.
                current?.Add(row);
            }

            return new SampleScript(lines);
        }

        public bool TryGetLines(string dialogueEntryId, out IReadOnlyList<string> lines)
        {
            if (dialogueEntryId != null && _lines.TryGetValue(dialogueEntryId, out List<string> found))
            {
                lines = found;
                return true;
            }

            lines = Array.Empty<string>();
            return false;
        }
    }
}
