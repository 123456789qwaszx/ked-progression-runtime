using System;
using System.Globalization;

namespace Ked.Progression.Save
{
    // 저장 파일에 찍는 값. 회차 ID와 슬롯 ID, 시각 표기가 한 자리에서 나온다.
    public static class SaveStamp
    {
        public static string NewId() => Guid.NewGuid().ToString("N");

        public static string NowUtc() =>
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }
}
