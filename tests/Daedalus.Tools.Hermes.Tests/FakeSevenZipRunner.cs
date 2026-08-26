using System.IO.Compression;

using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// 假 7z 桩（本机未必安装 7z，hermes.md §12 测试要点）：以真 zip 内容模拟 7z 的
/// a（压缩）/ t（校验）/ e（解压到临时目录）子命令，记录全部调用供断言。
/// </summary>
internal sealed class FakeSevenZipRunner : ISevenZipRunner
{
    /// <summary>PATH 探测结果；null 表示未安装 7z。</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>按归档路径判定压缩（a）是否失败；失败时写一个半成品文件后抛错。</summary>
    public Func<string, bool>? FailOnCompress { get; set; }

    /// <summary>按归档路径判定校验（t）是否失败。</summary>
    public Func<string, bool>? FailOnTest { get; set; }

    /// <summary>全部调用参数（按调用顺序）。</summary>
    public List<IReadOnlyList<string>> Invocations { get; } = [];

    /// <summary>e 子命令解压到的临时目录（供断言清理）。</summary>
    public List<string> ExtractedTempDirectories { get; } = [];

    public string? FindExecutable() => ExecutablePath;

    public Task RunAsync(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        Invocations.Add(arguments);
        switch (arguments[0])
        {
            case "a":
            {
                // 形状：["a", "-mx=9", archivePath, 裸文件名…]
                string archivePath = arguments[2];
                if (FailOnCompress?.Invoke(archivePath) == true)
                {
                    File.WriteAllText(archivePath, "压缩到一半的半成品");
                    throw new InvalidOperationException("假 7z：压缩失败");
                }

                using (var stream = new FileStream(archivePath, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (string name in arguments.Skip(3))
                    {
                        zip.CreateEntryFromFile(Path.Combine(workingDirectory!, name), name);
                    }
                }

                break;
            }
            case "t":
            {
                // 形状：["t", archivePath]
                string archivePath = arguments[1];
                if (FailOnTest?.Invoke(archivePath) == true)
                {
                    throw new InvalidOperationException("假 7z：校验失败");
                }

                try
                {
                    using var zip = ZipFile.OpenRead(archivePath);
                    _ = zip.Entries.Count;
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidOperationException("假 7z：归档不可读", ex);
                }

                break;
            }
            case "e":
            {
                // 形状：["e", "-y", "-o<目录>", archivePath]
                string outputDirectory = arguments[2]["-o".Length..];
                Directory.CreateDirectory(outputDirectory);
                ZipFile.ExtractToDirectory(arguments[3], outputDirectory);
                ExtractedTempDirectories.Add(outputDirectory);
                break;
            }
            default:
                throw new InvalidOperationException($"假 7z：不支持的子命令 {arguments[0]}");
        }

        return Task.CompletedTask;
    }
}
