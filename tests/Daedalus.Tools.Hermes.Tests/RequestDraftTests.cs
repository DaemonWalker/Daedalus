using System.Text.Json;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>RequestDraft：节点 ↔ 草稿映射与脏标记比较（FR-HERMES-012）。</summary>
public sealed class RequestDraftTests
{
    private static CollectionNode CreateNode() => new()
    {
        Type = CollectionNodeType.Request,
        Name = "登录",
        Method = "POST",
        Url = "{{host}}/api/login",
        Headers = [new KeyValueEntry("Content-Type", "application/json"), new KeyValueEntry("X-Off", "1", Enabled: false)],
        Body = new RequestBody { Kind = RequestBodyKind.Raw, ContentType = "application/json", Text = "{}" },
        Options = new RequestOptions(FollowRedirect: false, UseCookies: null),
        PostResponseScript = "pm.environment.set('a','1');",
    };

    [Fact]
    public void FromNodeToNode_往返_内容完整保留()
    {
        CollectionNode node = CreateNode();

        CollectionNode roundTripped = RequestDraft.FromNode(node).ToNode(node.Name);

        // record 含 List 成员时引用比较不可靠，用序列化比较往返一致性
        Assert.Equal(JsonSerializer.Serialize(node), JsonSerializer.Serialize(roundTripped));
    }

    [Fact]
    public void FromNode_缺省字段_填默认值()
    {
        var node = new CollectionNode { Type = CollectionNodeType.Request, Name = "空请求" };

        RequestDraft draft = RequestDraft.FromNode(node);

        Assert.Equal("GET", draft.Method);
        Assert.Equal(string.Empty, draft.Url);
        Assert.Empty(draft.Headers);
        Assert.Null(draft.Body);
        Assert.Null(draft.Options);
        Assert.Null(draft.PostResponseScript);
    }

    [Fact]
    public void ContentEquals_内容相同列表不同实例_视为未修改()
    {
        RequestDraft a = RequestDraft.FromNode(CreateNode());
        RequestDraft b = RequestDraft.FromNode(CreateNode());

        Assert.True(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_任一字段变化_视为已修改()
    {
        RequestDraft saved = RequestDraft.FromNode(CreateNode());

        Assert.False(saved.ContentEquals(saved with { Url = "http://other" }));
        Assert.False(saved.ContentEquals(saved with { Headers = [.. saved.Headers, new KeyValueEntry("A", "b")] }));
        Assert.False(saved.ContentEquals(saved with { Headers = [saved.Headers[0] with { Enabled = false }, saved.Headers[1]] }));
        Assert.False(saved.ContentEquals(saved with { Options = new RequestOptions(true, null) }));
        Assert.False(saved.ContentEquals(saved with { PostResponseScript = null }));
        Assert.False(saved.ContentEquals(null));
    }

    [Fact]
    public void ToNode_类型固定为Request且名称来自参数()
    {
        CollectionNode node = RequestDraft.Empty.ToNode("新请求");

        Assert.Equal(CollectionNodeType.Request, node.Type);
        Assert.Equal("新请求", node.Name);
    }
}
