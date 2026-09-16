namespace Ked.Progression
{
    public interface IProgressionLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    internal sealed class NullProgressionLog : IProgressionLog
    {
        public static readonly NullProgressionLog Instance = new();

        private NullProgressionLog()
        {
        }

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }
    }
}
