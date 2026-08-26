namespace Daedalus.Abstractions;

/// <summary>
/// 可选能力（FR-HERMES-012）：由 <see cref="ITool.CreateView"/> 返回的视图实现，
/// 主窗口在关闭其所在标签页或关闭主窗口前逐一咨询，任一视图拒绝则取消本次关闭。
/// </summary>
public interface IToolCloseConfirmation
{
    /// <summary>视图即将关闭时调用（可在此期间提示用户，如"有未保存的修改"）；返回 false 取消关闭。</summary>
    bool ConfirmClose();
}
