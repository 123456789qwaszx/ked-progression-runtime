using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ked.Progression
{
    public interface IChapterOptionsView
    {
        Task<int> ShowAsync(
            IReadOnlyList<ResolvedOption> options,
            int hiddenCount);

        void Cancel();
    }
}
