namespace Daedalus.Tools.Hermes.Collections;

/// <summary>集合树节点种类（hermes.md §11.1）：Folder=文件夹（可嵌套），Request=请求。</summary>
public enum CollectionNodeType
{
    /// <summary>文件夹，子节点在 <see cref="CollectionNode.Items"/>。</summary>
    Folder,

    /// <summary>请求，请求数据在 <see cref="CollectionNode"/> 的请求字段上。</summary>
    Request,
}
