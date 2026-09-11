using FarArc.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FarArc.Tests.Utils
{
    [TestClass]
    public sealed class VisibleItemReorderHelperTests
    {
        [TestMethod]
        public void TryMove_ReordersVisibleItemsAndKeepsHiddenSlots()
        {
            string[] fullOrder = ["a", "b", "c", "d", "e"];
            string[] visibleOrder = ["b", "d", "e"];

            var moved = VisibleItemReorderHelper.TryMove(
                fullOrder,
                visibleOrder,
                source: "e",
                target: "b",
                insertAfter: false,
                out var reordered);

            Assert.IsTrue(moved);
            CollectionAssert.AreEqual(new[] { "a", "e", "c", "b", "d" }, reordered);
        }

        [TestMethod]
        public void TryMove_ReturnsFalseWhenDropDoesNotChangeOrder()
        {
            string[] fullOrder = ["a", "b", "c", "d"];
            string[] visibleOrder = ["b", "d"];

            var moved = VisibleItemReorderHelper.TryMove(
                fullOrder,
                visibleOrder,
                source: "b",
                target: "d",
                insertAfter: false,
                out var reordered);

            Assert.IsFalse(moved);
            CollectionAssert.AreEqual(fullOrder, reordered);
        }

        [TestMethod]
        public void TryMove_RejectsVisibleItemsMissingFromFullOrder()
        {
            string[] fullOrder = ["a", "b", "c"];
            string[] visibleOrder = ["b", "missing"];

            var moved = VisibleItemReorderHelper.TryMove(
                fullOrder,
                visibleOrder,
                source: "b",
                target: "missing",
                insertAfter: true,
                out var reordered);

            Assert.IsFalse(moved);
            CollectionAssert.AreEqual(fullOrder, reordered);
        }
    }
}
