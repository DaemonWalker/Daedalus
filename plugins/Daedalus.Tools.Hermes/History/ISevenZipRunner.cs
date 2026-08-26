namespace Daedalus.Tools.Hermes.History;

/// <summary>
/// 7z 命令行调用抽象（hermes.md §10.2）：PATH 探测与进程执行收口于此，
/// 归档/搜索逻辑注入本接口以便测试用假桩覆盖 7z 路径（本机未必安装 7z）。
/// </summary>
internal interface ISevenZipRunner
{
    /// <summary>在 PATH 中依次探测 7z.exe、7za.exe；找到返回完整路径，未安装返回 null。</summary>
    string? FindExecutable();

    /// <summary>
    /// 执行一次 7z 命令（如 <c>a -mx=9</c> 压缩、<c>t</c> 校验、<c>e</c> 解压）。
    /// 退出码非零时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    Task RunAsync(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}
