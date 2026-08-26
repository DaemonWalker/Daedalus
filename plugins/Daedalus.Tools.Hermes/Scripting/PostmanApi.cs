using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Variables;

using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Daedalus.Tools.Hermes.Scripting;

/// <summary>
/// <c>pm</c> 宿主对象（hermes.md §7.2，FR-HERMES-042）：environment 读写 + response 访问。
/// 环境写操作先记在内存覆盖层与待落盘队列里（脚本内立即可见），脚本结束后由
/// <see cref="ScriptHost"/> 统一经 EnvironmentStore 持久化——避免在 Jint 同步执行中
/// 做同步等异步（规范 §5），同时保证脚本内写后读语义与 Postman 一致。
/// </summary>
public sealed class PostmanApi
{
    private readonly PostmanEnvironmentApi _environment;
    private readonly PostmanResponseApi _response;

    /// <param name="response">最终一跳的响应（FR-HERMES-045）。</param>
    /// <param name="activeEnvironment">当前启用环境；未启用时为 null（environment 写操作将报错）。</param>
    public PostmanApi(HopResponse response, HermesEnvironment? activeEnvironment)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = new PostmanResponseApi(response);
        _environment = new PostmanEnvironmentApi(activeEnvironment);
    }

    /// <summary><c>pm.environment</c>。</summary>
    public PostmanEnvironmentApi Environment => _environment;

    /// <summary><c>pm.response</c>。</summary>
    public PostmanResponseApi Response => _response;

    /// <summary><c>pm.sendRequest</c> 本期不实现（hermes.md §7.2 不支持项）。</summary>
    public void SendRequest(JsValue request) => throw NotImplemented(nameof(SendRequest));

    /// <summary><c>pm.test</c> 本期不实现。</summary>
    public void Test(JsValue name, JsValue callback) => throw NotImplemented(nameof(Test));

    /// <summary><c>pm.globals</c> 本期不实现：访问即抛错。</summary>
    public object Globals => throw NotImplemented(nameof(Globals));

    /// <summary>注入脚本引擎（pm.response.json() 需要借引擎的 JSON 解析器把响应体转为 JS 对象）。</summary>
    internal void AttachEngine(Engine engine) => _response.AttachEngine(engine);

    private static JavaScriptException NotImplemented(string api) =>
        new($"pm.{api} 本期未实现（仅支持 pm.environment.get/set/unset 与 pm.response.code/text/json/headers.get）");
}

/// <summary><c>pm.environment</c>：读写当前启用环境的变量。</summary>
public sealed class PostmanEnvironmentApi
{
    // 变量当前值：启用环境的 enabled 变量快照 + 脚本内写覆盖（与 VariableResolver 口径一致）
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly bool _hasActiveEnvironment;

    internal PostmanEnvironmentApi(HermesEnvironment? activeEnvironment)
    {
        _hasActiveEnvironment = activeEnvironment is not null;
        if (activeEnvironment is not null)
        {
            foreach (EnvironmentVariable variable in activeEnvironment.Variables)
            {
                if (variable.Enabled)
                {
                    _values[variable.Key] = variable.Value;
                }
            }
        }
    }

    /// <summary>待落盘的写操作队列（有序；IsSet=false 表示 unset）。由 <see cref="ScriptHost"/> 在脚本结束后消费。</summary>
    internal List<(bool IsSet, string Key, string? Value)> PendingOperations { get; } = [];

    /// <summary>写操作摘要（供"脚本输出"页展示）。</summary>
    internal List<string> MutationLog { get; } = [];

    /// <summary><c>pm.environment.get(key)</c>：读变量，不存在返回 undefined。</summary>
    public JsValue Get(string key) =>
        _values.TryGetValue(key, out string? value) ? new JsString(value) : JsValue.Undefined;

    /// <summary><c>pm.environment.set(key, value)</c>：写变量（脚本内立即生效，脚本结束后持久化）。</summary>
    public void Set(string key, JsValue value)
    {
        ThrowIfNoActiveEnvironment(nameof(Set));
        string text = TypeConverter.ToString(value);
        _values[key] = text;
        PendingOperations.Add((true, key, text));
        MutationLog.Add($"pm.environment.set(\"{key}\", \"{text}\")");
    }

    /// <summary><c>pm.environment.unset(key)</c>：删除变量（脚本内立即生效，脚本结束后持久化）。</summary>
    public void Unset(string key)
    {
        ThrowIfNoActiveEnvironment(nameof(Unset));
        _values.Remove(key);
        PendingOperations.Add((false, key, null));
        MutationLog.Add($"pm.environment.unset(\"{key}\")");
    }

    private void ThrowIfNoActiveEnvironment(string api)
    {
        if (!_hasActiveEnvironment)
        {
            throw new JavaScriptException($"pm.environment.{api} 失败：当前未启用任何环境");
        }
    }
}

/// <summary><c>pm.response</c>：访问最终一跳的响应。</summary>
public sealed class PostmanResponseApi
{
    private readonly HopResponse _response;
    private readonly PostmanHeadersApi _headers;
    private Engine? _engine;

    internal PostmanResponseApi(HopResponse response)
    {
        _response = response;
        _headers = new PostmanHeadersApi(response.Headers);
    }

    /// <summary><c>pm.response.code</c>：HTTP 状态码。</summary>
    public int Code => _response.Status;

    /// <summary><c>pm.response.headers</c>。</summary>
    public PostmanHeadersApi Headers => _headers;

    /// <summary><c>pm.response.text()</c>：响应体文本。</summary>
    public string Text() => _response.Body;

    /// <summary><c>pm.response.json()</c>：响应体解析为 JS 对象；非 JSON 时抛 SyntaxError（由脚本或沙箱捕获）。</summary>
    public JsValue Json()
    {
        if (_engine is null)
        {
            throw new JavaScriptException("pm.response.json() 内部错误：脚本引擎未注入");
        }

        // 借引擎的 JSON 解析器：得到真正的 JS 对象，解析失败按 JS 语义抛 SyntaxError
        return new Jint.Native.Json.JsonParser(_engine).Parse(_response.Body);
    }

    internal void AttachEngine(Engine engine) => _engine = engine;
}

/// <summary><c>pm.response.headers</c>。</summary>
public sealed class PostmanHeadersApi
{
    private readonly IReadOnlyList<History.NameValuePair> _headers;

    internal PostmanHeadersApi(IReadOnlyList<History.NameValuePair> headers) => _headers = headers;

    /// <summary><c>pm.response.headers.get(name)</c>：取响应头，大小写不敏感；不存在返回 undefined。</summary>
    public JsValue Get(string name)
    {
        string? value = _headers
            .FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return value is null ? JsValue.Undefined : new JsString(value);
    }
}
