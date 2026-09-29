using PdfSpace.Layout;

internal static class IndexWindowTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        IndexWindow empty = default;
        check(empty.TotalCount == 0 && empty.VisibleCount == 0 && empty.PageCount == 0 && empty.PageNumber == 0, "default index window is empty");
        check(!empty.HasNext && !empty.HasPrevious && !empty.Contains(0), "empty window has no navigation or contained items");
        check(empty.Move(int.MaxValue).VisibleCount == 0 && empty.Move(int.MinValue).VisibleCount == 0, "empty range navigation stays bounded");
        reject(() => empty.Reveal(0), "empty window rejects reveal");
        reject(() => new IndexWindow(-1), "index window rejects negative count");
        reject(() => new IndexWindow(10, -1), "index window rejects negative start");
        reject(() => new IndexWindow(10, 0, 0), "index window rejects zero capacity");
        var window = new IndexWindow(4096);
        check(window.VisibleCount == 64 && window.PageCount == 64 && window.PageNumber == 1 && window.HasNext, "dense index window realizes only one bounded range");
        check(window.Move(1).Start == 64 && window.Move(1).PageNumber == 2, "next index range is aligned");
        check(window.Move(int.MaxValue).Start == 4032 && !window.Move(int.MaxValue).HasNext, "range navigation clamps at last full page");
        check(window.Move(int.MaxValue).Move(int.MinValue).Start == 0, "extreme backwards navigation cannot overflow");
        check(window.Reveal(3000).Contains(3000) && window.Reveal(3000).Start == 2944, "reveal reaches objects past the former inspector limit");
        check(window.Reveal(30) == window, "revealing an already visible item preserves range identity");
        check(window.TryGetIndex(63, out var last) && last == 63 && !window.TryGetIndex(64, out _) && !window.TryGetIndex(-1, out _), "slot mapping validates pool boundaries");
        var partial = new IndexWindow(65).Move(1);
        check(partial.Start == 64 && partial.VisibleCount == 1 && partial.HasPrevious && !partial.HasNext, "last partial range contains only valid rows");
        check(partial.TryGetIndex(0, out last) && last == 64 && !partial.TryGetIndex(1, out _), "unused recycled slots cannot select stale objects");
        var maximum = new IndexWindow(int.MaxValue, int.MaxValue, int.MaxValue);
        check(maximum.Start == 0 && maximum.VisibleCount == int.MaxValue && maximum.PageCount == 1 && !maximum.Move(int.MaxValue).HasNext, "maximum count and capacity remain overflow safe");
        var random = new Random(544);
        var valid = true;
        for (var i = 0; i < 5000; i++)
        {
            var count = random.Next(1, 20001); var capacity = random.Next(1, 201); var item = random.Next(count);
            var candidate = new IndexWindow(count, random.Next(int.MaxValue), capacity).Reveal(item);
            valid &= candidate.Contains(item) && candidate.Start % capacity == 0 && candidate.VisibleCount > 0 &&
                candidate.VisibleCount <= capacity && candidate.Start + candidate.VisibleCount <= count &&
                candidate.PageNumber <= candidate.PageCount;
        }
        check(valid, "5000 random index ranges preserve reveal, alignment and collection bounds");
        var warm = new IndexWindow(20000); var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) warm = warm.Reveal(i % 20000).Move(i % 2 == 0 ? 1 : -1);
        check(GC.GetAllocatedBytesForCurrentThread() == before, "range navigation allocates no managed memory");
        GC.KeepAlive(warm);
    }
}
