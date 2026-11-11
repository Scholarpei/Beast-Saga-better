using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class ItemFeatureTests
{
    public static (int Checks, int Failures) Run()
    {
        var checks = 0;
        var failures = 0;
        void Test(string name, Action action)
        {
            checks++;
            try { action(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        void Require(bool condition, string message = "Assertion failed")
        {
            if (!condition) throw new Exception(message);
        }
        var item = new CheatItemEntry(Key: "item:ore", Name: "铁矿", Description: "Crafting MATERIAL", Category: 2,
            ItemType: 0, Rank: 1, IsEquipment: false);
        var equipment = new CheatItemEntry(Key: "equip:sword", Name: "铁剑", Description: "剑", Category: 1,
            ItemType: 0, Rank: 1, IsEquipment: true);
        ItemEditRequest Request(CheatItemEntry entry, ItemEditOperation operation = ItemEditOperation.Add,
            long quantity = 1, long? owned = 5) => new(Entry: entry, Operation: operation, Quantity: quantity,
            OwnedCount: owned, TemplateValid: true, CapacityAvailable: true);
        bool Allowed(ItemEditRequest request) => ItemEditPolicy.BlockReason(request) is null;

        Test("item search preserves same-name stable identities and source order", () =>
        {
            var second = item with { Key = "item:other", Description = "另一来源" };
            var filtered = CheatItemCatalog.Filter(new[] { second, equipment, item }, search: "铁矿");
            Require(filtered.Select(entry => entry.Key).SequenceEqual(new[] { "item:other", "item:ore" }));
            Require(CheatItemCatalog.Filter(new[] { item }, search: " material ").Count == 1);
            Require(CheatItemCatalog.Filter(new[] { item }, search: "missing").Count == 0);
        });
        Test("category and owned filters use stable keys with exact ownership", () =>
        {
            var sameName = item with { Key = "ITEM:ore" };
            var counts = new Dictionary<string, int> { ["item:ore"] = 2, [equipment.Key] = 0 };
            var result = CheatItemCatalog.Filter(new[] { sameName, item, equipment }, category: 2,
                onlyOwned: true, ownedCounts: counts);
            Require(result.Count == 1 && result[0].Key == "item:ore");
            Require(CheatItemCatalog.Filter(new[] { item }, onlyOwned: true).Count == 0);
            Require(CheatItemCatalog.Filter(new[] { item }, category: 999).Count == 0);
        });
        Test("task items and letters remain browsable but both operations are blocked", () =>
        {
            var task = item with { Category = 4 };
            var letter = item with { ItemType = 5, Key = "item:letter" };
            Require(CheatItemCatalog.Filter(new[] { task, letter }).Count == 2);
            foreach (var entry in new[] { task, letter })
                foreach (var operation in new[] { ItemEditOperation.Add, ItemEditOperation.Remove })
                    Require(!Allowed(Request(entry, operation)));
        });
        Test("ordinary quantity accepts exact cap and rejects zero negative and oversized values", () =>
        {
            Require(Allowed(Request(item, quantity: 1)));
            Require(Allowed(Request(item, quantity: 999)));
            foreach (var quantity in new long[] { 0, -1, 1000, int.MaxValue, (long)int.MaxValue + 1, long.MaxValue })
                Require(!Allowed(Request(item, quantity: quantity)), quantity.ToString());
        });
        Test("equipment additions accept ten but reject eleven", () =>
        {
            Require(Allowed(Request(equipment, quantity: 10)));
            Require(!Allowed(Request(equipment, quantity: 11)));
        });
        Test("ordinary removal never exceeds owned count", () =>
        {
            Require(Allowed(Request(item, ItemEditOperation.Remove, quantity: 5)));
            Require(!Allowed(Request(item, ItemEditOperation.Remove, quantity: 6)));
            Require(!Allowed(Request(item, ItemEditOperation.Remove, owned: 0)));
        });
        Test("inventory arithmetic rejects unknown negative oversized and overflowing counts", () =>
        {
            foreach (var owned in new long?[] { null, -1, (long)int.MaxValue + 1, long.MaxValue })
                Require(!Allowed(Request(item, owned: owned)));
            Require(Allowed(Request(item, owned: int.MaxValue - 1)));
            Require(!Allowed(Request(item, owned: int.MaxValue)));
            Require(Allowed(Request(item, ItemEditOperation.Remove, owned: int.MaxValue)));
        });
        Test("unknown categories types operations and incomplete templates fail closed", () =>
        {
            foreach (var invalid in new[] { item with { Category = 0 }, item with { Category = 9 },
                         item with { ItemType = -1 }, item with { ItemType = 10 }, item with { Rank = -1 },
                         item with { Key = "" }, item with { Name = " " }, item with { Description = null! } })
                Require(!Allowed(Request(invalid)));
            Require(!Allowed(Request(item, (ItemEditOperation)999)));
            Require(!Allowed(Request(item) with { TemplateValid = false }));
        });
        Test("additions require known capacity while removal needs no extra capacity", () =>
        {
            Require(!Allowed(Request(item) with { CapacityAvailable = null }));
            Require(!Allowed(Request(item) with { CapacityAvailable = false }));
            Require(Allowed(Request(item, ItemEditOperation.Remove) with { CapacityAvailable = null }));
        });
        var first = new CheatEquipmentInstance(Key: "ptr:100", ItemKey: equipment.Key, IsWorn: false, IsLocked: false);
        var secondInstance = first with { Key = "ptr:200" };
        ItemEditRequest RemoveEquipment(params string[] keys) => Request(equipment, ItemEditOperation.Remove,
            quantity: keys.Length, owned: 2) with
        { SelectedInstanceKeys = keys, EquipmentInstances = new[] { first, secondInstance } };
        Test("equipment deletion selects distinct exact instances including same-model copies", () =>
        {
            Require(Allowed(RemoveEquipment("ptr:100")));
            Require(Allowed(RemoveEquipment("ptr:100", "ptr:200")));
            Require(!Allowed(RemoveEquipment("ptr:100", "ptr:100")));
            Require(!Allowed(RemoveEquipment("ptr:100") with { Quantity = 2 }));
            Require(!Allowed(RemoveEquipment("ptr:100", "ptr:200") with { OwnedCount = 1 }));
        });
        Test("equipment deletion rejects worn locked or unknown protection state", () =>
        {
            foreach (var unsafeInstance in new[] { first with { IsWorn = true }, first with { IsLocked = true },
                         first with { IsWorn = null }, first with { IsLocked = null } })
                Require(!Allowed(RemoveEquipment(first.Key) with { EquipmentInstances = new[] { unsafeInstance } }));
        });
        Test("equipment deletion rejects stale foreign ambiguous and absent instance selection", () =>
        {
            Require(!Allowed(RemoveEquipment("old:100")));
            Require(!Allowed(RemoveEquipment("PTR:100")));
            Require(!Allowed(RemoveEquipment(first.Key) with { EquipmentInstances = new[] { first with { ItemKey = "equip:other" } } }));
            Require(!Allowed(RemoveEquipment(first.Key) with { EquipmentInstances = new[] { first, first } }));
            Require(!Allowed(RemoveEquipment(first.Key) with { SelectedInstanceKeys = null }));
            Require(!Allowed(RemoveEquipment(first.Key) with { EquipmentInstances = null }));
        });
        Test("checkpoint failure remains unprotected and allows a later successful retry", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(!session.EnsureProtection(token, () => null, _ => true));
            Require(!session.IsProtected && session.ProtectedGeneration is null);
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(session.EnsureProtection(token, () => "g1", generation => generation == "g1"));
            Require(session.IsProtected && session.ProtectedGeneration == "g1");
            session.EndSubmission(token);
            session.Reset();
            Require(!session.IsProtected && !session.IsBusy && session.ProtectedGeneration is null);
        });
        Test("created checkpoint must pass real validation before it protects edits", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(!session.EnsureProtection(token, () => "g1", _ => false));
            Require(!session.IsProtected);
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(!session.EnsureProtection(token, () => " ", _ => true));
            Require(!session.IsProtected);
        });
        Test("protected submissions reuse their generation and revalidate before each write", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(session.EnsureProtection(token, () => "g1", _ => true));
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            var validated = "";
            Require(session.EnsureProtection(token, () => throw new Exception("Checkpoint recreated"), generation =>
            { validated = generation; return true; }));
            Require(validated == "g1" && session.ProtectedGeneration == "g1");
        });
        Test("failed reused checkpoint is discarded and only the next submission can rebuild", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(session.EnsureProtection(token, () => "g1", _ => true));
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(!session.EnsureProtection(token, () => throw new Exception("Silent fallback"), _ => false));
            Require(!session.IsProtected && session.ProtectedGeneration is null);
            Require(!session.EnsureProtection(token, () => "same-submission-fallback", _ => true));
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(session.EnsureProtection(token, () => "g2", _ => true));
            Require(session.ProtectedGeneration == "g2");
        });
        Test("busy item submission rejects reentry and only its owner may unlock", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(session.IsBusy && !session.TryBeginSubmission(out _));
            session.EndSubmission(token + 1);
            Require(session.IsBusy);
            session.EndSubmission(token);
            Require(!session.IsBusy && session.TryBeginSubmission(out _));
        });
        Test("protection callbacks cannot run without an active submission", () =>
        {
            var session = new ItemEditSession();
            Require(!session.EnsureProtection(0, () => throw new Exception("Unexpected create"), _ => true));
            Require(session.TryBeginSubmission(out var token));
            session.EndSubmission(token);
            Require(!session.EnsureProtection(token, () => throw new Exception("Unexpected create"), _ => true));
        });
        Test("reset invalidates completed protection and old submission cannot unlock the new one", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var oldToken));
            Require(session.EnsureProtection(oldToken, () => "g1", _ => true));
            session.Reset();
            Require(!session.IsProtected && session.ProtectedGeneration is null);
            Require(session.TryBeginSubmission(out var newToken));
            Require(newToken != oldToken);
            session.EndSubmission(oldToken);
            Require(session.IsBusy);
            Require(!session.EnsureProtection(oldToken, () => "old", _ => true));
            Require(session.EnsureProtection(newToken, () => "g2", _ => true));
        });
        Test("reset within checkpoint creation cannot restore protection or authorize an old write", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(!session.EnsureProtection(token, () => { session.Reset(); return "g1"; }, _ => true));
            Require(!session.IsProtected && !session.IsBusy);
        });
        Test("reset within initial checkpoint validation rejects stale protection", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(!session.EnsureProtection(token, () => "g1", _ => { session.Reset(); return true; }));
            Require(!session.IsProtected && session.ProtectedGeneration is null);
        });
        Test("reset within reused checkpoint validation cannot clobber a newer session", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(session.EnsureProtection(token, () => "g1", _ => true));
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(!session.EnsureProtection(token, () => "unused", _ =>
            {
                session.Reset();
                Require(session.TryBeginSubmission(out var newerToken));
                Require(session.EnsureProtection(newerToken, () => "g2", _ => true));
                return true;
            }));
            Require(session.IsProtected && session.IsBusy && session.ProtectedGeneration == "g2");
        });
        Test("checkpoint exceptions fail closed without poisoning retry", () =>
        {
            var session = new ItemEditSession();
            Require(session.TryBeginSubmission(out var token));
            Require(!session.EnsureProtection(token, () => throw new IOException("disk"), _ => true));
            Require(!session.IsProtected);
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(!session.EnsureProtection(token, () => "g1", _ => throw new IOException("corrupt")));
            Require(!session.IsProtected);
            session.EndSubmission(token);
            Require(session.TryBeginSubmission(out token));
            Require(session.EnsureProtection(token, () => "g2", _ => true));
        });
        Test("icon generations retain pending loans until observed and reject stale binding", () =>
        {
            var queue = new ItemIconBudget(2, 2);
            queue.SetVisible(new[] { "a" });
            queue.BeginFrame();
            Require(queue.TryStart("a", 0, out var old));
            queue.SetVisible(new[] { "b" });
            Require(!queue.CanBind(old));
            Require(queue.ReleaseCompleted().Count == 0);
            Require(queue.Complete(old));
            Require(queue.ReleaseCompleted().SequenceEqual(new[] { old }));
            Require(!queue.CanBind(old));
        });
        Test("icon capacity includes pending and per-frame count and elapsed budgets", () =>
        {
            var queue = new ItemIconBudget(3, 2);
            queue.SetVisible(new[] { "a", "b", "c", "d" });
            queue.BeginFrame();
            Require(queue.TryStart("a", 0, out _));
            Require(!queue.TryStart("b", 2, out _));
            Require(queue.TryStart("b", 1, out _));
            Require(!queue.TryStart("c", 1, out _));
            queue.BeginFrame();
            Require(queue.TryStart("c", 0, out _));
            Require(!queue.TryStart("d", 0, out _));
            queue.SetVisible(Array.Empty<string>());
            Require(queue.ReleaseCompleted().Count == 0 && queue.Count == 3);
        });
        Test("failed item checkpoint never executes commit callback", () =>
        {
            var session = new ItemEditSession();
            var writes = 0;
            Require(!ItemSubmission.TryExecute(session, () => null, _ => true, () => true, () => writes++));
            Require(writes == 0 && !session.IsBusy);
            Require(!ItemSubmission.TryExecute(session, () => "g", _ => true, () => false, () => writes++));
            Require(writes == 0 && !session.IsBusy);
            Require(ItemSubmission.TryExecute(session, () => "g", _ => true, () => true, () => writes++));
            Require(writes == 1);
        });
        Test("reset during final revalidation rejects commit even if callback returns true", () =>
        {
            var session = new ItemEditSession();
            var writes = 0;
            Require(!ItemSubmission.TryExecute(session, () => "g", _ => true, () => { session.Reset(); return true; }, () => writes++));
            Require(writes == 0);
        });
        Test("uncertain native icon return remains charged and is never retried", () =>
        {
            var budget = new ItemIconBudget(1, 2);
            budget.SetVisible(new[] { "old" }); budget.BeginFrame();
            Require(budget.TryStart("old", 0, out var ticket));
            budget.Complete(ticket); budget.SetVisible(new[] { "new" });
            var attempts = 0;
            try { budget.TryReturn(ticket, () => { attempts++; throw new IOException("native release uncertain"); }); }
            catch (IOException) { }
            Require(budget.Count == 1 && budget.QuarantinedCount == 1);
            Require(!budget.TryReturn(ticket, () => attempts++));
            Require(budget.ReleaseCompleted().Count == 0 && attempts == 1);
            budget.BeginFrame(); Require(!budget.TryStart("new", 0, out _));
        });
        Test("late native completion drains after owner destruction on independent main-thread heartbeat", () =>
        {
            foreach (var success in new[] { true, false })
            {
                var budget = new ItemIconBudget(1, 2);
                var pump = new ItemIconCompletionPump(budget, mainThreadId: 7);
                budget.SetVisible(new[] { "pending" }); budget.BeginFrame();
                Require(budget.TryStart("pending", 0, out var ticket));
                budget.SetVisible(Array.Empty<string>()); // Owning RuntimeDriver destroyed; it never ticks again.
                var returns = 0; var observed = false; var observations = 0;
                pump.Drain(7, _ => returns++, (_, _) => throw new Exception("Unexpected release failure"));
                Require(returns == 0 && budget.Count == 1);
                pump.Observe(ticket, () => { observed = success; observations++; });
                pump.Drain(99, _ => returns++, (_, _) => throw new Exception("Unexpected release failure"));
                Require(returns == 0 && budget.Count == 1); // Off-main cannot touch native ownership.
                pump.Drain(7, _ => returns++, (_, _) => throw new Exception("Unexpected release failure"));
                Require(returns == 1 && budget.Count == 0 && observed == success && observations == 1);
                pump.Observe(ticket);
                pump.Drain(7, _ => returns++, (_, _) => throw new Exception("Unexpected release failure"));
                Require(returns == 1);
            }
        });
        Test("large equipment display cache does not enumerate or allocate on unchanged draw reads", () =>
        {
            var enumerated = 0;
            IEnumerable<CheatEquipmentInstance> Inventory()
            {
                for (var index = 0; index < 20000; index++)
                {
                    enumerated++;
                    yield return new CheatEquipmentInstance("instance:" + index, equipment.Key, false, index == 0);
                }
            }
            var index = new ItemEquipmentIndex(Inventory());
            var cache = new ItemDisplayPolicyCache();
            cache.SetDraft(equipment.Key, "1"); cache.SetSelected("instance:19999", true);
            var displayRequest = Request(equipment, owned: 20000);
            cache.Update(displayRequest, index, snapshotVersion: 1);
            Require(cache.Ready && cache.AddReason == null && cache.RemoveReason == null);
            cache.Update(displayRequest, index, snapshotVersion: 1); // Warm generic equality/JIT before measuring steady reads.
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var draw = 0; draw < 1000; draw++)
            {
                cache.Update(displayRequest, index, snapshotVersion: 1);
                Require(cache.Ready && cache.RemoveReason == null && cache.IsSelected("instance:19999"));
            }
            Require(GC.GetAllocatedBytesForCurrentThread() == allocated && enumerated == 20000);
            cache.SetSelected("instance:19999", false); cache.SetSelected("instance:0", true);
            Require(!cache.Ready);
            cache.Update(Request(equipment, owned: 20000), index, snapshotVersion: 1);
            Require(cache.Ready && cache.RemoveReason != null && enumerated == 20000);
            cache.SetDraft(equipment.Key, "0");
            cache.Update(Request(equipment, owned: 20000), index, snapshotVersion: 1);
            Require(cache.AddReason != null && cache.RemoveReason != null);
            cache.SetDraft(equipment.Key, "1");
            var unlocked = new ItemEquipmentIndex(new[] { new CheatEquipmentInstance("instance:0", equipment.Key, false, false) });
            cache.Update(displayRequest, unlocked, snapshotVersion: 2);
            Require(cache.RemoveReason == null); // New snapshot invalidates the formerly locked result.
            var ambiguous = new ItemEquipmentIndex(new[] { first with { Key = "instance:0" }, first with { Key = "instance:0" } });
            cache.Update(displayRequest, ambiguous, snapshotVersion: 3);
            Require(cache.RemoveReason != null);
        });
        return (checks, failures);
    }
}
