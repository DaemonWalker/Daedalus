using Daedalus.Abstractions;
using Daedalus.Hosting;

using Serilog;

namespace Daedalus.App;

/// <summary>组合根（架构 §6）：创建 Serilog、PluginLoader、ToolHost、每工具独立 ServiceProvider 与主窗口。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string baseDirectory = AppContext.BaseDirectory;

        // 日志级别经 daedalus.json 配置（架构 §6.2）：解析发生在 Serilog 初始化之前，
        // 解析警告先收集，待日志器建好后补记
        var loggingWarnings = new List<string>();
        LoggingSettings loggingSettings = LoggingBootstrap.Load(
            Path.Combine(baseDirectory, LoggingBootstrap.ConfigFileName), loggingWarnings);
        Log.Logger = LoggingBootstrap.CreateConfiguration(baseDirectory, loggingSettings).CreateLogger();
        foreach (string warning in loggingWarnings)
        {
            Log.Warning("日志配置：{Warning}", warning);
        }

        try
        {
            ApplicationConfiguration.Initialize();

            // 未处理异常兜底（架构 §8）：记日志 + 友好提示，尽量不退出（NFR-003）
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            var loader = new PluginLoader(Log.Logger);
            PluginCatalog catalog = loader.LoadFromDirectory(Path.Combine(baseDirectory, "plugins"));
            var host = new ToolHost(baseDirectory, Log.Logger, catalog.Formatters);

            // 每工具独立 ServiceProvider（架构 §6，step 14）：失败隔离，不中断其他工具
            ToolContainerRegistry containers = ToolContainerRegistry.Build(catalog.Tools, host, Log.Logger);

            Log.Information(
                "启动完成：加载 {ToolCount} 个工具、{FormatterCount} 个格式化器，{FailureCount} 个插件加载失败，{ContainerFailureCount} 个工具容器构建失败",
                catalog.Tools.Count,
                catalog.Formatters.Count,
                catalog.Failures.Count,
                containers.Failures.Count);
            foreach (ITool tool in catalog.Tools)
            {
                Log.Information("已加载工具 {ToolId}（{DisplayName} {Version}）", tool.Metadata.Id, tool.Metadata.DisplayName, tool.Metadata.Version);
            }

            Application.Run(new MainForm(
                catalog,
                host,
                containers,
                Log.Logger,
                Path.Combine(baseDirectory, LoggingBootstrap.ConfigFileName),
                loggingSettings));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "主程序发生未处理异常，即将退出");
            MessageBox.Show($"Daedalus 发生未处理异常，即将退出。\n\n{ex.Message}", "Daedalus", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        Log.Error(e.Exception, "界面线程发生未处理异常");
        MessageBox.Show($"操作失败：{e.Exception.Message}", "Daedalus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "非界面线程发生未处理异常");
        }
    }
}
