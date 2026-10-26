using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Ked.Progression.Save
{
    // ## 설정 변경 금지.

    // C# 객체와 JSON간 통일된 변환 규칙.
    // (표현 형식 책임)
    // 로컬 저장 파일은 camelCase를 사용한다.
    // 딕셔너리 키는 그대로 - CamelCasePropertyNamesContractResolver를 그냥 쓰면 스탯 키까지 바뀜.
    public static class SaveJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    // 필드 이름은 camelCase로 하되, 내부 dictionary key는 그대로 둔다.
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = true,
                },
            },
            // 값이 null이여도 JSON에 필드를 남긴다.
            NullValueHandling = NullValueHandling.Include,
        };

        // 응답 안에 그대로 실려 온 JSON(스냅샷)을 같은 규약으로 되읽을 때.
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        // 깊은 복사와 진단 출력에 사용하는 한 줄 형식.
        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        // 사람이 읽기 편하게. 로컬 저장에 사용.
        public static string SerializePretty(object value) =>
            JsonConvert.SerializeObject(value, Formatting.Indented, Settings);

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
