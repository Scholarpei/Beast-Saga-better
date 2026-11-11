using System.Diagnostics;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using BeastSagaBetter.Core;
using UnityEngine;

namespace BeastSagaBetter;

internal static class ItemIcons
{
    private sealed class Loan
    {
        internal ItemIconTicket Ticket;
        internal MyGLoader Loader = null!;
        internal bool Complete, Failed;
        internal bool Quarantined;
        internal Texture? Texture;
        internal Rect Uv;
        internal float Aspect = 1;
        internal string Note = "加载中";
        internal string Url = "";
    }
    private static readonly ItemIconBudget budget = new();
    private static readonly Dictionary<long, Loan> loans = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, Loan> registry = new();
    private static ItemIconCompletionPump? completionPump;
    private static string[] visible = Array.Empty<string>();
    internal static string[]? TestVisible;
    private static bool enabled;
    private static string observerStatus = "尚未安装";
    private static long acquired, released, successes, failures;
    private static int lastStarts, maxStarts;
    private static double maxRequestMs;
    private static readonly Queue<double> recentTicks = new();
    internal static object Metrics => new { enabled, observerStatus, held = budget.Count, quarantined = budget.QuarantinedCount, pending = loans.Values.Count(loan => !loan.Complete), ready = loans.Values.Count(loan => loan.Complete && !loan.Quarantined), acquired, released, successes, failures, maxRequestMs, lastStarts, maxStarts,
        scope = "ItemIcons.Tick active frames only; excludes panel drawing and other plugin work", samples = recentTicks.Count,
        p95TickMs = recentTicks.Count == 0 ? 0 : recentTicks.OrderBy(value => value).ElementAt((int)((recentTicks.Count - 1) * .95)),
        icons = loans.Values.Select(loan => new { loan.Ticket.Key, loan.Ticket.Id, loan.Url, loan.Complete, loan.Failed, loan.Quarantined, loan.Note, pointer = loan.Loader.Pointer.ToInt64().ToString("X"), bindable = budget.CanBind(loan.Ticket),
            texture = loan.Complete && !loan.Failed && !loan.Quarantined && loan.Loader.texture != null ? new { pointer = loan.Loader.texture.Pointer.ToInt64().ToString("X"), loan.Loader.texture.refCount, loan.Loader.texture.rotated, loan.Loader.texture.width, loan.Loader.texture.height,
                alpha = loan.Loader.texture.alphaTexture != null, uv = loan.Loader.texture.uvRect.ToString(), offset = loan.Loader.texture.offset.ToString(), originalSize = loan.Loader.texture.originalSize.ToString() } : null }).ToArray() };
    internal static void Initialize(Harmony harmony)
    {
        try
        {
            completionPump = new ItemIconCompletionPump(budget, Environment.CurrentManagedThreadId);
            var success = AccessTools.Method(typeof(MyGLoader), "OnLoadSuccess", new[] { typeof(NTexture) });
            var fail = AccessTools.Method(typeof(MyGLoader), "OnLoadFail", new[] { typeof(string) });
            var heartbeat = AccessTools.Method(typeof(StageEngine), "LateUpdate", Type.EmptyTypes);
            if (success == null || fail == null || heartbeat == null) throw new MissingMethodException("MyGLoader callbacks / native StageEngine.LateUpdate");
            harmony.Patch(success, postfix: new HarmonyMethod(typeof(ItemIcons), nameof(Success)));
            harmony.Patch(fail, postfix: new HarmonyMethod(typeof(ItemIcons), nameof(Fail)));
            harmony.Patch(heartbeat, postfix: new HarmonyMethod(typeof(ItemIcons), nameof(NativeHeartbeat)));
            if (Harmony.GetPatchInfo(success)?.Owners.Contains(harmony.Id) != true || Harmony.GetPatchInfo(fail)?.Owners.Contains(harmony.Id) != true || Harmony.GetPatchInfo(heartbeat)?.Owners.Contains(harmony.Id) != true) throw new InvalidOperationException("callback / native drain patch verification");
            enabled = true; observerStatus = "两个完成回调与独立原生主线程清理已挂钩";
        }
        catch (Exception error) { enabled = false; observerStatus = error.Message; Plugin.Logger.LogWarning("图标借用停用：" + error); }
    }
    private static void Success(MyGLoader __instance) => Observe(__instance, false);
    private static void Fail(MyGLoader __instance) => Observe(__instance, true);
    // FairyGUI's game-owned engine survives RuntimeDriver destruction. Its main-thread
    // heartbeat remains patched while any registered late callback can still complete.
    private static void NativeHeartbeat() => Release();
    private static void Observe(MyGLoader loader, bool failed)
    {
        if (!registry.TryGetValue(loader.Pointer, out var loan) || loan.Complete) return;
        completionPump?.Observe(loan.Ticket, () =>
        {
            loan.Complete = true; loan.Failed = failed;
            if (failed) { failures++; loan.Note = "图片暂不可用"; } else successes++;
        });
    }
    internal static void RequestVisible(IEnumerable<string> keys)
    {
        visible = keys.Distinct(StringComparer.Ordinal).Take(64).ToArray();
    }
    internal static void Clear()
    {
        visible = Array.Empty<string>(); TestVisible = null; budget.SetVisible(visible);
        Release();
    }
    private static void Release()
    {
        completionPump?.Drain(Environment.CurrentManagedThreadId, ticket =>
        {
            // Only constructor failure has a budget ticket without a native loader/URL.
            if (!loans.TryGetValue(ticket.Id, out var loan)) return;
            var pointer = loan.Loader.Pointer;
            loan.Loader.Dispose();
            registry.TryRemove(pointer, out _);
            loans.Remove(ticket.Id);
            released++;
        }, (ticket, error) =>
        {
            if (loans.TryGetValue(ticket.Id, out var loan))
            {
                loan.Quarantined = true; loan.Texture = null;
                loan.Note = "归还状态不明，已隔离且计入容量；本次运行不重试";
            }
            Plugin.Logger.LogWarning("图标借用归还异常，保留隔离所有权：" + error);
        });
    }
    internal static void Tick(bool active)
    {
        var watch = Stopwatch.StartNew();
        lastStarts = 0;
        if (Plugin.TestMode && TestVisible != null) visible = TestVisible.Take(64).ToArray();
        budget.SetVisible(active ? visible : Array.Empty<string>());
        Release();
        foreach (var loan in loans.Values)
        {
            if (!loan.Complete || loan.Failed || loan.Texture != null || !budget.CanBind(loan.Ticket)) continue;
            try
            {
                var texture = loan.Loader.texture;
                if (texture == null || texture.disposed || texture.nativeTexture == null) { loan.Note = "图片暂不可用"; loan.Failed = true; continue; }
                // Ordinary IMGUI has no verified FairyGUI separate-alpha/rotated pass. Fail visibly rather than draw an atlas incorrectly.
                if (texture.rotated || texture.alphaTexture != null || texture.offset.x != 0 || texture.offset.y != 0 || texture.originalSize.x != texture.width || texture.originalSize.y != texture.height)
                { loan.Note = "图片暂不可显示（旋转/独立透明/裁剪）"; loan.Failed = true; continue; }
                loan.Texture = texture.nativeTexture; loan.Uv = texture.uvRect;
                loan.Aspect = texture.height > 0 ? (float)texture.width / texture.height : 1;
                loan.Note = "原生图标";
            }
            catch (Exception error) { loan.Note = "图片暂不可用"; loan.Failed = true; Plugin.Logger.LogWarning(error.Message); }
        }
        budget.BeginFrame();
        if (active && enabled)
        {
            foreach (var key in visible)
            {
                if (!budget.TryStart(key, watch.Elapsed.TotalMilliseconds, out var ticket)) continue;
                lastStarts++; maxStarts = Math.Max(maxStarts, lastStarts);
                Loan loan;
                try { loan = new Loan { Ticket = ticket, Loader = new MyGLoader() }; }
                catch (Exception error)
                {
                    // No URL was assigned; there is no native request to wait for.
                    budget.Complete(ticket); budget.SetVisible(Array.Empty<string>()); Release();
                    Plugin.Logger.LogWarning("图标构造失败：" + error); break;
                }
                loans.Add(ticket.Id, loan); registry.TryAdd(loan.Loader.Pointer, loan); acquired++;
                var requestWatch = Stopwatch.StartNew();
                try
                {
                    loan.Url = ItemEditor.IconUrl(key);
                    if (string.IsNullOrWhiteSpace(loan.Url)) { Observe(loan.Loader, true); continue; }
                    // Register before assignment: native manager may invoke callback synchronously in set_url.
                    loan.Loader.url = loan.Url;
                }
                catch (Exception error)
                {
                    // Unknown native completion remains retained and bounded. Never timeout/dispose a possibly pending borrow.
                    loan.Note = "原生加载异常，等待完成观察";
                    Plugin.Logger.LogWarning("图标请求异常：" + error);
                }
                finally { maxRequestMs = Math.Max(maxRequestMs, requestWatch.Elapsed.TotalMilliseconds); }
            }
        }
        if (active) recentTicks.Enqueue(watch.Elapsed.TotalMilliseconds);
        while (recentTicks.Count > 512) recentTicks.Dequeue();
    }
    internal static string Draw(Rect rect, string key)
    {
        var loan = loans.Values.FirstOrDefault(value => value.Ticket.Key == key && budget.CanBind(value.Ticket));
        if (loan?.Texture == null) return loan?.Note ?? (enabled ? "等待图片" : "图片暂不可用");
        var width = Math.Min(rect.width, rect.height * loan.Aspect);
        var height = Math.Min(rect.height, rect.width / loan.Aspect);
        GUI.DrawTextureWithTexCoords(new Rect(rect.center.x - width / 2, rect.center.y - height / 2, width, height), loan.Texture, loan.Uv, true);
        return "";
    }
}
