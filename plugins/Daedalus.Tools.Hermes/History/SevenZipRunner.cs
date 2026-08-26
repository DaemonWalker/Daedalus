using System.Diagnostics;

namespace Daedalus.Tools.Hermes.History;

/// <summary>
/// 默认 <see cref="ISevenZipRunner"/>：PATH 探测（依次 7z.exe、7za.exe），
/// 经 <see cref="Process"/> 调用并等待退出，非零退出码视为失败。
/// </summary>
internal sealed class SevenZipRunner : ISevenZipRunner
{
    private static readonly string[] CandidateNames = ["7z.exe", "7za.exe"];

    /// <inheritdoc />
    public string? FindExecutable()
    {
        // PATH 探测：逐一目录尝试候选名，返回第一个真实存在的完整路径
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in CandidateNames)
            {
                string candidate = Path.Combine(directory.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task RunAsync(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"无法启动 7z 进程：{executablePath}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"无法启动 7z 进程：{executablePath}（{ex.Message}）", ex);
        }

        // 先排干输出再取 ExitCode，避免子进程写满管道缓冲后死锁
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消等待不等于进程退出：解压半途的 7z 会占着临时目录导致调用方清理失败，
            // 必须先杀进程树并等它真正退出再向上抛取消
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 进程恰好已退出，无需处理
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        string error = await stderr.ConfigureAwait(false);
        await stdout.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"7z 执行失败（退出码 {process.ExitCode}）：{error.Trim()}");
        }
    }
}
