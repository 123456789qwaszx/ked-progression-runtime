namespace Ked.Progression
{
    public interface IRollbackHistory
    {
        int LastHistoryIndex { get; }
        bool TryTakeRollbackTarget(out int historyIndex);
    }
}
