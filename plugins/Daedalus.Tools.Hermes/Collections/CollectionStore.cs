using Daedalus.Tools.Hermes.Persistence;

namespace Daedalus.Tools.Hermes.Collections;

/// <summary>单个损坏文件的恢复信息（DR-003）。</summary>
/// <param name="FilePath">损坏文件的原路径。</param>
/// <param name="BackupFilePath">备份后的路径。</param>
public sealed record CorruptedFileRecovery(string FilePath, string BackupFilePath);

/// <summary>集合全量读取结果。</summary>
/// <param name="Collections">成功读出的集合。</param>
/// <param name="Recoveries">损坏并已备份恢复的文件清单；为空表示未发生恢复。</param>
public sealed record CollectionStoreLoadResult(
    IReadOnlyList<HermesCollection> Collections,
    IReadOnlyList<CorruptedFileRecovery> Recoveries);

/// <summary>
/// 集合持久化（hermes.md §11.1）：数据目录下 collections/&lt;id&gt;.json，一集合一文件。
/// 单个文件损坏时按 DR-003 备份该文件并跳过，不影响其余集合。
/// </summary>
public sealed class CollectionStore
{
    private readonly string _collectionsDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    public CollectionStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _collectionsDirectory = Path.Combine(dataDirectory, "collections");
    }

    /// <summary>读取全部集合；目录不存在返回空清单，损坏文件逐个备份恢复并记入 <see cref="CollectionStoreLoadResult.Recoveries"/>。</summary>
    public async Task<CollectionStoreLoadResult> LoadAllAsync()
    {
        var collections = new List<HermesCollection>();
        var recoveries = new List<CorruptedFileRecovery>();
        if (!Directory.Exists(_collectionsDirectory))
        {
            return new CollectionStoreLoadResult(collections, recoveries);
        }

        // 文件名排序保证加载顺序稳定（id 为 ULID，近似按创建时间排序）
        foreach (string filePath in Directory.EnumerateFiles(_collectionsDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            JsonDataFileLoadResult<HermesCollection> result = await JsonDataFile.LoadAsync<HermesCollection>(filePath, IsValid).ConfigureAwait(false);
            if (result.BackupFilePath is not null)
            {
                recoveries.Add(new CorruptedFileRecovery(filePath, result.BackupFilePath));
            }
            else if (result.Value is not null)
            {
                collections.Add(result.Value);
            }
        }

        return new CollectionStoreLoadResult(collections, recoveries);
    }

    /// <summary>保存集合（覆盖写对应文件）。</summary>
    public async Task SaveAsync(HermesCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (!IsValid(collection))
        {
            throw new ArgumentException("集合 id/name 不能为空，且 id 只能包含字母、数字、-、_。", nameof(collection));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await JsonDataFile.SaveAsync(GetFilePath(collection.Id), collection).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>删除集合对应的文件；文件不存在时不报错。</summary>
    public Task DeleteAsync(string collectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        string filePath = GetFilePath(collectionId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string id) => Path.Combine(_collectionsDirectory, id + ".json");

    // id 直接作为文件名，必须排除路径分隔等非法字符
    private static bool IsValid(HermesCollection collection) =>
        !string.IsNullOrWhiteSpace(collection.Id)
        && !string.IsNullOrWhiteSpace(collection.Name)
        && collection.Id.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_');
}
