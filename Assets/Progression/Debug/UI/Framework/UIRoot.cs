using System;

namespace Ked.Progression.Debugging.UI
{
    public interface IUIRoot { }

    public abstract class UIRoot<TRefs> : UIBase<TRefs>, IUIRoot
        where TRefs : struct, Enum
    {
    }
}
