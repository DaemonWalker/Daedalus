# 版本记录

版本号格式：`大版本.小版本.bug修复`。最新版本在最上方。

## 0.3.0（2026-08-26）

- 第 2 步「契约 + 插件加载」完成：Abstractions 契约定稿（ToolMetadata / ITool / IToolHost / FormatOptions / IFormatter，架构 §4）；Hosting 新增 PluginLoader（平铺扫描 plugins/*.dll、每插件独立可收集 AssemblyLoadContext、契约与 Serilog 共享宿主上下文、内存流加载不锁文件、失败隔离 + 失败清单）与 PluginCatalog / PluginLoadFailure；新增两个测试桩插件工程（正常 / 静态构造抛异常），Hosting 单测 5 项全绿

## 0.2.0（2026-08-26）

- 第 1 步「工程脚手架」完成：Daedalus.sln + 12 个工程（Abstractions / Hosting / App 及 Hermes、Proteus、Formatters.Json、Formatters.Xml 插件与 5 个镜像测试工程）；.editorconfig、Directory.Build.props（net10.0-windows / Nullable / AnalysisLevel / 警告即错误）、Directory.Packages.props 中央包管理（Serilog、Jint、FastColoredTextBox.NET10、xUnit 等）就位；插件工程生成后拷贝产物至 App 输出 plugins/；App 可运行出空白主窗口；构建与冒烟测试全绿

## 0.1.0（2026-08-26）

- 设计阶段完成：需求、架构、代码规范、插件设计、实施计划与执行协议定稿；尚未开始编码
