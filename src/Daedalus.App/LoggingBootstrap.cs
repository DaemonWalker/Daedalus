using System.Text.Json;

using Serilog;
using Serilog.Events;

namespace Daedalus.App;

/// <summary>
/// 日志配置（架构 §6.2）：默认级别 + 按 SourceContext 前缀（即插件 id）的 override 表。
/// </summary>
/// <param name="DefaultLevel">默认最低级别。</param>
/// <param name="Overrides">SourceContext 前缀 → 最低级别；键约定为插件 id（如 daedalus.tools.hermes）。</param>
internal sealed record LoggingSettings(LogEventLevel DefaultLevel, IReadOnlyDictionary<string, LogEventLevel> Overrides);

/// <summary>
/// 程序目录 daedalus.json 的日志节解析与 Serilog 管道构建（架构 §6.2）。
/// 文件缺失、JSON 损坏、级别字符串无法识别时一律回退默认并收集警告（由调用方在
/// 日志器建好后补记 Warning——解析发生在 Serilog 初始化之前，彼时无日志器可用），不中断启动。
/// </summary>
internal static class LoggingBootstrap
{
    /// <summary>配置文件名（位于程序目录根）。</summary>
    public const string ConfigFileName = "daedalus.json";

    /// <summary>默认配置：Information、无 override。</summary>
    public static LoggingSettings Default { get; } = new(LogEventLevel.Information, new Dictionary<string, LogEventLevel>());

    /// <summary>
    /// 读取 <paramref name="filePath"/> 的 logging 节；文件缺失返回 <see cref="Default"/>（不视为错误）。
    /// 任何解析问题都记入 <paramref name="warnings"/> 并对该条目回退默认。
    /// </summary>
    public static LoggingSettings Load(string filePath, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (!File.Exists(filePath))
        {
            return Default;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            warnings.Add($"{ConfigFileName} 解析失败（{ex.Message}），日志使用默认配置");
            return Default;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("logging", out JsonElement logging))
            {
                // 配置文件存在但没有 logging 节：视为只想配置其他内容，日志用默认
                return Default;
            }

            LogEventLevel defaultLevel = ParseLevel(logging, "default", Default.DefaultLevel, warnings);

            var overrides = new Dictionary<string, LogEventLevel>();
            if (logging.TryGetProperty("overrides", out JsonElement overridesElement) && overridesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in overridesElement.EnumerateObject())
                {
                    if (TryParseLevel(entry.Value, out LogEventLevel level))
                    {
                        overrides[entry.Name] = level;
                    }
                    else
                    {
                        warnings.Add($"{ConfigFileName} 中 override \"{entry.Name}\" 的级别 \"{entry.Value}\" 无法识别，该条目已忽略");
                    }
                }
            }

            return new LoggingSettings(defaultLevel, overrides);
        }
    }

    /// <summary>按配置构建 Serilog 管道：滚动文件 sink（按天、保留 14 天）+ 默认级别 + override。</summary>
    public static LoggerConfiguration CreateConfiguration(string baseDirectory, LoggingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LoggerConfiguration configuration = new LoggerConfiguration().MinimumLevel.Is(settings.DefaultLevel);
        foreach ((string source, LogEventLevel level) in settings.Overrides)
        {
            // Override 按 SourceContext 前缀匹配：约定 ToolHost.GetLogger 以插件 id 作 SourceContext，
            // 因此 override 键直接写插件 id（注意 Ordinal 前缀匹配，大小写需与插件 id 一致）
            configuration.MinimumLevel.Override(source, level);
        }

        return configuration.WriteTo.File(
            Path.Combine(baseDirectory, "logs", "daedalus-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14);
    }

    private static LogEventLevel ParseLevel(JsonElement parent, string propertyName, LogEventLevel fallback, List<string> warnings)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            return fallback;
        }

        if (TryParseLevel(element, out LogEventLevel level))
        {
            return level;
        }

        warnings.Add($"{ConfigFileName} 中 logging.{propertyName} 的级别 \"{element}\" 无法识别，已回退 {fallback}");
        return fallback;
    }

    private static bool TryParseLevel(JsonElement element, out LogEventLevel level)
    {
        level = default;
        return element.ValueKind == JsonValueKind.String
            && Enum.TryParse(element.GetString(), ignoreCase: true, out level);
    }
}
