using System.Collections.Generic;
using System.Linq;

namespace FarArc.Utils
{
    /// <summary>
    /// Reorders the visible projection of a list while keeping filtered-out items in their
    /// original slots. This lets a filtered list update its complete persisted order without
    /// dropping or unexpectedly moving hidden entries.
    /// </summary>
    public static class VisibleItemReorderHelper
    {
        public static bool TryMove<T>(
            IReadOnlyList<T> fullOrder,
            IReadOnlyList<T> visibleOrder,
            T source,
            T target,
            bool insertAfter,
            out List<T> reorderedFullOrder,
            IEqualityComparer<T>? comparer = null)
            where T : notnull
        {
            comparer ??= EqualityComparer<T>.Default;
            reorderedFullOrder = fullOrder.ToList();

            if (comparer.Equals(source, target) || fullOrder.Count == 0 || visibleOrder.Count < 2)
                return false;

            var fullSet = new HashSet<T>(fullOrder, comparer);
            var visibleSet = new HashSet<T>(visibleOrder, comparer);
            if (fullSet.Count != fullOrder.Count
                || visibleSet.Count != visibleOrder.Count
                || visibleOrder.Any(item => !fullSet.Contains(item))
                || fullOrder.Count(item => visibleSet.Contains(item)) != visibleOrder.Count)
            {
                return false;
            }

            var reorderedVisible = visibleOrder.ToList();
            var sourceIndex = IndexOf(reorderedVisible, source, comparer);
            var targetIndex = IndexOf(reorderedVisible, target, comparer);
            if (sourceIndex < 0 || targetIndex < 0)
                return false;

            reorderedVisible.RemoveAt(sourceIndex);
            targetIndex = IndexOf(reorderedVisible, target, comparer);
            if (targetIndex < 0)
                return false;

            var insertIndex = targetIndex + (insertAfter ? 1 : 0);
            reorderedVisible.Insert(insertIndex, source);
            if (visibleOrder.SequenceEqual(reorderedVisible, comparer))
                return false;

            var nextVisibleIndex = 0;
            for (var i = 0; i < fullOrder.Count; i++)
            {
                if (visibleSet.Contains(fullOrder[i]))
                    reorderedFullOrder[i] = reorderedVisible[nextVisibleIndex++];
            }

            return nextVisibleIndex == reorderedVisible.Count;
        }

        private static int IndexOf<T>(IReadOnlyList<T> items, T value, IEqualityComparer<T> comparer)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (comparer.Equals(items[i], value))
                    return i;
            }

            return -1;
        }
    }
}
