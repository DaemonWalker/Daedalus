using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

using Jint;
using Jint.Runtime;

using Serilog;

namespace Daedalus.Tools.Hermes.Scripting;

/// <summary>
/// 后事件脚本执行结果。
/// </summary>
/// <param name="Error">脚本错误信息（JS 异常 / 沙箱超限）；null 表示执行成功。异常不中断主流程（FR-HERMES-043）。</param>
/// <param name="UpdatedEnvironmentData">脚本写环境变量后的完整环境数据（已持久化）；无写操作时为 null。</param>
/// <param name="MutationLog">环境写操作摘要，供"脚本输出"页展示。</param>
public sealed record ScriptExecutionResult(
    string? Error,
    EnvironmentData? UpdatedEnvironmentData,
    IReadOnlyList<string> MutationLog);

/// <summary>
/// Jint 后事件脚本宿主（hermes.md §7，FR-HERMES-040~045）：每次执行新建沙箱 Engine
/// （内存/超时取自设置，NFR-002），不开启 AllowClr、只注入 pm 一个宿主对象。
/// 脚本异常隔离为结果中的 <see cref="ScriptExecutionResult.Error"/>；环境写操作在脚本结束后
/// 统一经 <see cref="EnvironmentStore"/> 立即持久化（FR-HERMES-044）。
/// </summary>
public sealed class ScriptHost(EnvironmentStore environmentStore, ILogger logger)
{
    /// <summary>
    /// 执行后事件脚本。<paramref name="response"/> 为最终一跳的响应（FR-HERMES-045）。
    /// 本方法只在调用方取消时抛 <see cref="OperationCanceledException"/>，其余异常全部隔离进结果。
    /// </summary>
    public async Task<ScriptExecutionResult> RunAsync(
        string script,
        HopResponse response,
        EnvironmentData environmentData,
        HermesSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(environmentData);
        ArgumentNullException.ThrowIfNull(settings);

        var api = new PostmanApi(response, environmentData.FindActive());
        string? error = null;
        logger.Debug("开始执行后事件脚本（{ScriptLength} 字符，状态 {Status}）", script.Length, response.Status);
        try
        {
            var engine = new Engine(options => options
                .LimitMemory(settings.ScriptMemoryLimitBytes)
                .TimeoutInterval(TimeSpan.FromMilliseconds(settings.ScriptTimeoutMs))
                .CancellationToken(cancellationToken));
            api.AttachEngine(engine);
            engine.SetValue("pm", api);
            engine.Execute(script);
        }
        catch (OperationCanceledException)
        {
            // 用户取消发送：与发送流程一致的取消语义，不算脚本错误
            throw;
        }
        catch (JintException ex)
        {
            // JS 异常（含 pm 未实现 API 抛错）与沙箱超限（内存/超时）
            error = ex.Message;
            logger.Warning("后事件脚本执行失败：{ScriptError}", ex.Message);
        }
        catch (Exception ex)
        {
            // 插件边界兜底（规范 §7 / 架构 §9）：宿主侧意外异常同样隔离，不中断发送主流程
            error = ex.Message;
            logger.Error(ex, "后事件脚本宿主异常");
        }

        // 环境写操作统一落盘：脚本内已生效的变更在结束后立即持久化（FR-HERMES-044），
        // 即使脚本后半段出错，已执行的 set/unset 仍然生效（与 Postman 语义一致）
        EnvironmentData? updated = null;
        if (api.Environment.PendingOperations.Count > 0 && environmentData.ActiveId is { } activeId)
        {
            foreach ((bool isSet, string key, string? value) in api.Environment.PendingOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                updated = isSet
                    ? await environmentStore.SetVariableAsync(activeId, key, value!).ConfigureAwait(false)
                    : await environmentStore.UnsetVariableAsync(activeId, key).ConfigureAwait(false);
            }
        }

        logger.Debug("后事件脚本执行结束：{Outcome}，环境写操作 {MutationCount} 条",
            error is null ? "成功" : "失败", api.Environment.MutationLog.Count);
        return new ScriptExecutionResult(error, updated, api.Environment.MutationLog);
    }
}
