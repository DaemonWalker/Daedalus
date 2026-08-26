using Daedalus.Abstractions;
using Daedalus.Hosting;

using Serilog;

namespace Daedalus.App;

/// <summary>组合根（架构 §6）：手工组合 Serilog、PluginLoader、ToolHost 与主窗口，不使用 DI 容器。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string baseDirectory = AppContext.BaseDirectory;

        // Serilog 滚动文件（NFR-001）：logs/daedalus-*.log，按天滚动，保留 14 天
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(baseDirectory, "logs", "daedalus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        try
        {
            ApplicationConfiguration.Initialize();

            // 未处理异常兜底（架构 §8）：记日志 + 友好提示，尽量不退出（NFR-003）
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            var loader = new PluginLoader(Log.Logger);
            PluginCatalog catalog = loader.LoadFromDirectory(Path.Combine(baseDirectory, "plugins"));
            var host = new ToolHost(baseDirectory, Log.Logger, catalog.Formatters);

            Log.Information(
                "启动完成：加载 {ToolCount} 个工具、{FormatterCount} 个格式化器，{FailureCount} 个插件加载失败",
                catalog.Tools.Count,
                catalog.Formatters.Count,
                catalog.Failures.Count);
            foreach (ITool tool in catalog.Tools)
            {
                Log.Information("已加载工具 {ToolId}（{DisplayName} {Version}）", tool.Metadata.Id, tool.Metadata.DisplayName, tool.Metadata.Version);
            }

            Application.Run(new MainForm(catalog, host, Log.Logger));
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
