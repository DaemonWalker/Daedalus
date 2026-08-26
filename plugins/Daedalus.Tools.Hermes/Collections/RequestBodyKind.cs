namespace Daedalus.Tools.Hermes.Collections;

/// <summary>请求体种类（hermes.md §11.1，FR-HERMES-002）。</summary>
public enum RequestBodyKind
{
    /// <summary>raw 文本，内容在 <see cref="RequestBody.Text"/>，Content-Type 在 <see cref="RequestBody.ContentType"/>。</summary>
    Raw,

    /// <summary>x-www-form-urlencoded，字段表在 <see cref="RequestBody.Fields"/>。</summary>
    UrlEncoded,
}
