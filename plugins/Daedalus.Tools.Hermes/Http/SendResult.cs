namespace Daedalus.Tools.Hermes.Http;

/// <summary>
/// 一次发送的完整结果：按顺序收集的跳转链（至少一跳）+ 异常终止标记。
/// 最终一跳为 <see cref="Hops"/> 的最后一项。
/// </summary>
/// <param name="Hops">跳转链，每跳含请求与响应快照。</param>
/// <param name="RedirectLimitExceeded">true 表示超过跳转上限（10 跳）后停止，链中最后一跳仍是重定向。</param>
/// <param name="RedirectLoopDetected">true 表示跳转链中出现完全相同的 URL，已停止跟随。</param>
public sealed record SendResult(
    IReadOnlyList<ResponseHop> Hops,
    bool RedirectLimitExceeded,
    bool RedirectLoopDetected)
{
    /// <summary>最终一跳（链至少含一跳，由 HttpEngine 保证）。</summary>
    public ResponseHop FinalHop => Hops[^1];
}
