using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Ked.Progression.Samples
{
    /// <summary>
    /// 이 샘플이 진행 저장의 HostState 칸에 싣는 것.
    ///
    /// 진행 저장(Ked.Progression.Save)은 호스트 상태를 문자열 한 칸으로만 받고 안을 보지 않는다.
    /// 실제 게임(ked-presentation-runtime)은 여기에 백로그·대사 줄 좌표·Yarn 선택 기록을 싣는다.
    /// 이 샘플은 더 가볍다.
    ///
    /// - 회차(PlaythroughSave.HostState)    대사 기록 전체
    /// - 장면(SceneRecord.HostState)         없음 — 이 샘플의 대사에는 선택지가 없다
    /// - 재생 계획(SavedLoadPlan.HostState)  장면 안 몇 번째 줄까지 빨리 감을지
    /// </summary>
    public static class SampleHostState
    {
        private sealed class LoadDto
        {
            public int LineInScene;
        }

        public const string Scene = "";

        public static string Playthrough(IReadOnlyList<SampleLine> lines) =>
            JsonConvert.SerializeObject(lines);

        public static List<SampleLine> ReadPlaythrough(string hostState)
        {
            if (string.IsNullOrEmpty(hostState))
                return new List<SampleLine>();

            List<SampleLine> lines = Read<List<SampleLine>>(hostState, "대사 기록") ?? new List<SampleLine>();

            int previous = -1;
            foreach (SampleLine line in lines)
            {
                if (line == null || line.Serial <= previous)
                    throw new InvalidDataException("저장된 대사 기록의 줄 번호가 잘못됐다.");
                previous = line.Serial;
            }

            return lines;
        }

        public static string Load(int lineInScene) =>
            JsonConvert.SerializeObject(new LoadDto { LineInScene = lineInScene });

        public static int ReadLoad(string hostState)
        {
            LoadDto dto = Read<LoadDto>(hostState, "재생 계획");

            if (dto == null || dto.LineInScene < 0)
                throw new InvalidDataException("저장된 재생 계획의 줄 번호가 잘못됐다.");

            return dto.LineInScene;
        }

        private static T Read<T>(string json, string what)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"저장된 {what}을(를) 읽지 못했다: {error.Message}");
            }
        }
    }
}
