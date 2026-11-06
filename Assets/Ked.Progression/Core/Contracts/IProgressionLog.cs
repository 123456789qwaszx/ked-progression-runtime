namespace Ked.Progression
{
    public interface IProgressionLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
