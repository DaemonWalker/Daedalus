# Daedalus

个人日常开发工具箱。Windows 桌面应用（.NET 10 + WinForms），插件化架构——主程序是一个极薄的外壳，全部功能以插件形式挂载，在主窗口中统一入口、以标签页形式使用。

## 内置工具

| 工具 | 说明 |
|---|---|
| **Hermes** | 类 Postman 的 HTTP 客户端：请求编辑与发送、集合树管理、环境变量（`{{变量}}` 引用与悬浮就地编辑）、重定向跳转链逐跳查看、Cookie 共享、Postman Collection / Environment 与 cURL 导入、JavaScript 后事件脚本（Jint 沙箱，`pm` API 子集）、历史记录按天存储与按月归档、全文搜索 |
| **Proteus** | 多格式文本格式化工具：美化 / 压缩 / 校验（错误定位行列）、语法高亮；支持的格式由格式化器插件提供（第一方：JSON、XML） |
| **Cadmus** | 编码工具：Base64、URL 编码 |
| **Oedipus** | 解码工具：Base64、URL、XML 实体、JWT 解码 |

工具以古希腊神话 / 荷马史诗中职能相近的人物命名。

## 架构要点

- 两级插件：**工具插件（`ITool`）** 拥有主界面；**格式化器插件（`IFormatter`）** 是无界面的格式化能力，被 Proteus、Hermes 等工具复用
- 启动时扫描程序目录 `plugins/` 下平铺的 dll，每个插件装入独立 `AssemblyLoadContext`，单个插件加载失败不影响主程序与其他插件
- 插件只依赖 `Daedalus.Abstractions` 契约程序集，插件间复用经 `IToolHost` 契约（如 `FindFormatter("json")`）
- 每个工具拥有独立 IoC 容器（`Microsoft.Extensions.DependencyInjection`），插件自行注册内部服务
- 新增工具 / 格式化器：实现契约接口，编译产物放入 `plugins/`，重启应用即生效

## 构建与运行

环境要求：Windows 10+，.NET 10 SDK。

```bash
dotnet build        # 构建整个解决方案（插件产物自动部署到主程序输出目录 plugins/）
dotnet test         # 运行全部 xUnit 测试
```

也可使用一键脚本 `build.ps1` / `build.bat`。构建后直接运行 `src/Daedalus.App/bin/` 输出目录下的 `Daedalus.App.exe`，F5 调试即可用。

## 数据与日志

- 用户数据：程序目录 `data/`，按工具 id 划分子目录，全部为可读、可手工编辑的 JSON 文件（历史记录为 JSON Lines）
- 日志：Serilog 滚动文件 `logs/`，按天滚动保留 14 天；级别经程序目录 `daedalus.json` 配置
- 数据文件损坏时自动备份原文件（`.broken-时间戳`）并以空数据启动

## 源码结构

```
src/
  Daedalus.Abstractions/     # 契约程序集：ITool / IFormatter / IToolHost
  Daedalus.Hosting/          # 插件扫描、加载、生命周期
  Daedalus.App/              # WinForms 主程序（exe）
plugins/                     # 第一方工具与格式化器插件
tests/                       # xUnit 测试，与 src / plugins 镜像
docs/                        # 需求、架构、代码规范、插件设计文档
```

## 文档

- [需求规格说明书](docs/requirements.md)
- [架构设计](docs/architecture.md)
- [代码规范](docs/coding-style.md)
- [插件设计文档](docs/plugins/)
- [版本记录](version.md)
