using System.IO;
using System.Text;

namespace Ked.Progression.Save
{
    // (파일 I/O 안정성 책임)
    // 기존 파일을 직접 덮어쓰지 않는다.
    // 임시 파일에 먼저 전부 기록한 뒤 교체해,
    // 쓰기 도중 종료되더라도 기존 정상 파일이 손상될 가능성을 줄인다.
    public static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public static void WriteAllText(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            string tmp = path + ".tmp";

            File.WriteAllText(tmp, contents, Utf8NoBom);

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        public static string ReadAllTextOrNull(string path) =>
            File.Exists(path) 
                ? File.ReadAllText(path, Encoding.UTF8) 
                : null;
    }
}
