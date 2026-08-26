namespace Daedalus.Tools.Hermes.Tests;

/// <summary>IdGenerator 测试：ULID 格式（26 字符 Crockford base32）、唯一性、时间戳前缀可排序。</summary>
public sealed class IdGeneratorTests
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void NewId_任意调用_返回26字符且全部在Crockford字母表内()
    {
        string id = IdGenerator.NewId();

        Assert.Equal(26, id.Length);
        Assert.All(id, c => Assert.Contains(c, Alphabet));
    }

    [Fact]
    public void NewId_连续生成_互不重复()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(ids.Add(IdGenerator.NewId()));
        }
    }

    [Fact]
    public void NewId_时间戳前缀_晚生成的时间戳段字典序不早于先生成的()
    {
        // 前 10 字符为 48 位毫秒时间戳段；同一毫秒内随机段无顺序保证，故只比较时间戳段
        string first = IdGenerator.NewId();
        string second = IdGenerator.NewId();

        Assert.True(string.CompareOrdinal(second[..10], first[..10]) >= 0);
    }
}
