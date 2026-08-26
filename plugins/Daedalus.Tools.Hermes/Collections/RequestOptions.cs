namespace Daedalus.Tools.Hermes.Collections;

/// <summary>请求级选项覆盖（hermes.md §11.1）：null=继承全局设置，true/false=请求级覆盖。</summary>
/// <param name="FollowRedirect">是否跟随重定向（FR-HERMES-006）。</param>
/// <param name="UseCookies">是否使用共享 CookieContainer（FR-HERMES-007）。</param>
public sealed record RequestOptions(bool? FollowRedirect, bool? UseCookies);
