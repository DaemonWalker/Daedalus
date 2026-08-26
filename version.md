# 版本记录

版本号格式：`大版本.小版本.bug修复`。最新版本在最上方。

## 0.7.0（2026-08-27）

- 第 6 步「Hermes 数据层」完成：内部模型（集合树 CollectionNode 单类 Folder/Request、请求体 raw/urlEncoded、options 请求级覆盖、环境/历史/设置模型，均带 version 字段）按 hermes.md §11 实现；四个 Store——CollectionStore（collections/<id>.json 读写删、逐文件 DR-003 备份恢复）、EnvironmentStore（environments.json 读写 + Set/Unset 变量立即持久化）、HistoryStore（按天 jsonl 追加、信号量保护、响应体 UTF-8 字节上限截断并置 bodyTruncated、按天读取行级容错）、HermesSettingsStore（settings.json 默认值与损坏恢复）；DR-003 公共读写助手 Persistence/JsonDataFile；VariableResolver（{{var}} 替换、未定义原样保留 + 清单、\{{ 转义、无启用环境视为全未定义）；IdGenerator 自实现 ULID 格式 id；Hermes.Tests 47 用例替换占位冒烟；hermes.md 同步三处偏差（urlencoded body 存储形状、历史行级容错、§4 结构）；全部测试 116 项全绿

## 0.6.0（2026-08-27）

- 第 5 步「Proteus 工具」完成：格式化工具端到端可用，验证 ITool + IFormatter 两级插件联动。ProteusTool + ProteusPanel（输入/输出分栏、格式与缩进下拉、格式化/压缩/校验/清空/复制、状态栏错误行列、FastColoredTextBox 高亮：json 自定义规则 / xml 内置规则）；非 UI 逻辑独立可测——ProteusOperations（操作编排、FormatException 按校验失败处理、初始格式解析）与 ProteusSettingsStore（settings.json 读写、DR-003 损坏备份恢复）；插件私有依赖 FastColoredTextBox 随部署目标拷入 plugins/；Proteus.Tests 21 用例替换占位冒烟；App 进程冒烟 + 反射驱动端到端冒烟（格式化/校验/重启恢复格式与缩进）替代手工验收；全部测试 70 项全绿

## 0.5.0（2026-08-27）

- 第 4 步「JSON / XML 格式化器」完成：实现两个第一方 IFormatter 插件——JsonFormatter（System.Text.Json，严格 JSON，Utf8JsonWriter 输出、缩进可配、深度上限放宽，错误含 1 起始字符行列）与 XmlFormatter（XDocument + XmlReader，禁 DTD/外部实体防 XXE，声明头按原文保留，缩进可配）；两个测试工程各 16/17 用例覆盖设计文档全部测试要点；Hosting.Tests 新增第一方格式化器插件枚举集成验证（PluginLoader 扫描入表），真实 App 启动日志确认 2 个格式化器入表；全部测试 50 项全绿

## 0.4.0（2026-08-26）

- 第 3 步「主程序外壳」完成：主窗口实现工具列表 + 可关闭标签页容器（双击开工具、×/中键关标签页，FR-SHELL-002/003）与状态栏插件加载失败清单（FR-SHELL-004）；ToolHost 实现 IToolHost（数据目录分配、Serilog 日志器工厂、格式化器查询，FR-SHELL-005）；组合根接入 Serilog 按天滚动日志（logs/，保留 14 天，NFR-001）与 ThreadException 兜底（记日志 + 友好提示）；新建 Daedalus.App.Tests（ToolHost 8 用例），全部测试 17 项全绿

## 0.3.0（2026-08-26）

- 第 2 步「契约 + 插件加载」完成：Abstractions 契约定稿（ToolMetadata / ITool / IToolHost / FormatOptions / IFormatter，架构 §4）；Hosting 新增 PluginLoader（平铺扫描 plugins/*.dll、每插件独立可收集 AssemblyLoadContext、契约与 Serilog 共享宿主上下文、内存流加载不锁文件、失败隔离 + 失败清单）与 PluginCatalog / PluginLoadFailure；新增两个测试桩插件工程（正常 / 静态构造抛异常），Hosting 单测 5 项全绿

## 0.2.0（2026-08-26）

- 第 1 步「工程脚手架」完成：Daedalus.sln + 12 个工程（Abstractions / Hosting / App 及 Hermes、Proteus、Formatters.Json、Formatters.Xml 插件与 5 个镜像测试工程）；.editorconfig、Directory.Build.props（net10.0-windows / Nullable / AnalysisLevel / 警告即错误）、Directory.Packages.props 中央包管理（Serilog、Jint、FastColoredTextBox.NET10、xUnit 等）就位；插件工程生成后拷贝产物至 App 输出 plugins/；App 可运行出空白主窗口；构建与冒烟测试全绿

## 0.1.0（2026-08-26）

- 设计阶段完成：需求、架构、代码规范、插件设计、实施计划与执行协议定稿；尚未开始编码
