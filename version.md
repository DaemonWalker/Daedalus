# 版本记录

版本号格式：`大版本.小版本.bug修复`。最新版本在最上方。

## 1.8.0（2026-08-28）

- Hermes 请求头增强：默认头补 Cache-Control: no-cache（对齐 Postman 现行默认头全集）；Headers 页顶部新增自动计算行——Host 由 URL 推导（uri.Authority，含 {{变量}} 或非绝对 URL 时不显示）、Content-Length 按请求体 UTF-8 字节数估算、Content-Type 按请求体种类推导（与引擎 StringContent 口径一致），只读灰显、不进草稿/不保存、不可删除，可编辑行有同键启用项时对应自动行隐藏；KeyValueGrid 新增 SetAutoRows（行 Tag 标记 + UserDeletingRow 拦截）。引擎同名键规则：以最下方（最后出现）的启用项为准（OrdinalIgnoreCase 归键，非生效位置同名项跳过），与 Content-Type 去重逻辑叠加后同名单值头恒发一个值。Hermes.Tests 新增 2 例（同名头最下方生效/最下方禁用取上方启用项）并更新默认头断言；全部测试 320 项全绿

## 1.7.0（2026-08-28）

- Hermes 请求头与响应区改进：修复显式 Content-Type 头与请求体所选 Content-Type 重复发出的问题（引擎组装时显式头先移除再添加，单值头只发一个值）；新建请求预填默认请求头（Accept: \*/\*、Accept-Encoding: gzip, deflate, br、User-Agent: Daedalus-Hermes、Connection: keep-alive，Headers 页可编辑/停用）；新增 ResponseBodyDecoder——响应体按 Content-Encoding 解压（gzip/deflate/br，多层按施加逆序，deflate 兼容 zlib 封装与 raw，服务器标错编码时保留原始字节）再按 charset/BOM 解码，刻意不开 handler 的 AutomaticDecompression（它会向用户指定的 Accept-Encoding 并集自己的值），保证请求头原样发出；切换集合树请求时清空响应区，并按方法 + URL 精确匹配回填最近一次历史响应（ResponsePanel 显示版本号防止异步回填覆盖新状态，历史 URL 为变量替换后的值，含 {{变量}} 的模板 URL 匹配不到）。Hermes.Tests 新增 18 例（Content-Type 去重 2 例/Connection 原样发送/ResponseBodyDecoder 9 例/默认头预填/FindLatest 3 例/gzip 回环 2 例）；全部测试 318 项全绿

## 1.6.0（2026-08-27）

- 第 17 步「Oedipus 解码工具」完成：新增解码工具插件 Oedipus（俄狄浦斯，id `daedalus.tools.oedipus`，显示名「Oedipus 解码」）——支持 Base64（`Convert.FromBase64String` → 严格 UTF-8 还原，非法格式/非 UTF-8 字节明确报错）、URL（`Uri.UnescapeDataString`）、XML 实体（`WebUtility.HtmlDecode`，覆盖预定义实体与十/十六进制数字实体）解码（FR-OEDIPUS-002）；JWT 解码（header/payload 经 Base64Url 解码后 System.Text.Json 美化输出——缩进 2、`UnsafeRelaxedJsonEscaping` 中文不转义；签名段不解码、原样标注于输出末尾；段数不符/非法 Base64Url/段内非法 JSON 均明确报错，FR-OEDIPUS-003）；界面为方式下拉（DropDownList）+ 解码/清空/复制输出 + SplitContainer 左右多行 TextBox + StatusStrip（FR-OEDIPUS-001）；settings.json（`{ "version": 1, "lastDecoding": "base64" }`）记住上次解码方式、启动恢复（FR-OEDIPUS-004），DR-003 损坏备份恢复，字段缺失/未知 id 容忍回落默认。纯 BCL 实现不新增第三方依赖；OedipusOperations 非 UI 可测（方式清单、错误收敛为状态文本、初始方式解析），OedipusTool/OedipusPanel 走架构 §6.0 注册范式（transient + scope 随标签页生灭）。Oedipus.Tests 27 例（四类解码向量/错误路径/JWT 完整解码/设置往返/DR-003/Tool 冒烟）；文档同步——新建 oedipus.md（含 §5.1 JWT 输出形态），requirements §1/§5.6、architecture §2/§6.1/§7、coding-style §2.1、plan README；全部测试 300 项全绿

## 1.5.0（2026-08-27）

- 第 16 步「Cadmus 编码工具」完成：新增编码工具插件 Cadmus（卡德摩斯，id `daedalus.tools.cadmus`，显示名「Cadmus 编码」）——支持 Base64（UTF-8 字节序列 `Convert.ToBase64String`）与 URL（`Uri.EscapeDataString`）编码（FR-CADMUS-002）；界面为方式下拉（DropDownList）+ 编码/清空/复制输出 + SplitContainer 左右多行 TextBox（输入可编辑/输出只读）+ StatusStrip（FR-CADMUS-001）；settings.json（`{ "version": 1, "lastEncoding": "base64" }`）记住上次编码方式、启动恢复（FR-CADMUS-003），DR-003 损坏备份恢复，字段缺失/未知 id 容忍回落默认。纯 BCL 实现不新增第三方依赖（普通 TextBox，不引 FastColoredTextBox）；CadmusOperations 非 UI 可测（方式清单、错误收敛为状态文本、初始方式解析），CadmusTool/CadmusPanel 走架构 §6.0 注册范式（transient + scope 随标签页生灭）。Cadmus.Tests 18 例（编码向量/设置往返/DR-003/Tool 冒烟）；文档同步——新建 cadmus.md，requirements §1/§5.5、architecture §2/§6.1/§7、coding-style §2.1、plan README；全部测试 273 项全绿

## 1.4.1（2026-08-27）

- 修复系统显示缩放（高 DPI）下界面控件裁剪与错位：实测 AutoScaleMode 对手工构建的 Form/UserControl 均不生效，新增 `DpiScale` helper（App 与 Hermes 各一份 internal，避免改引用关系）——顶层窗口与工具视图按 96 DPI 基准显式 `Scale(DeviceDpi/96)` 一次，缩放后递归重置 AutoSize 控件消除双重放大（GrowOnly 需清零重测；TextBox/NumericUpDown 的 AutoSize 只管高度，宽度保留缩放结果）；嵌套 UserControl 不自行缩放、动态创建控件用 Dock/AutoSize，约定入架构 §6.4（docs/ 不入库）；Hermes 地址栏改 TableLayoutPanel 随窗口拉伸、发送/保存按钮改 AutoSize；build.bat 注释转 GBK 编码（cmd 中文正常显示）；全部测试 255 项全绿

## 1.4.0（2026-08-27）

- 第 15 步「Hermes 布局持久化」完成：HermesSettings 新增可空 `layout` 节（HermesLayout record，主/左/右三个分隔条的**比例**而非像素，向后兼容、Version 保持 1）；HermesPanel 三个 SplitContainer 挂 SplitterMoved（拖动结束）按比例即存（FR-HERMES-061 语义），Load 尺寸就绪后按比例还原并 clamp 到 Panel1MinSize/Panel2MinSize，双守卫防误触发（`_restoringLayout` 拦还原期回写、`_layoutLoaded` 拦初始化布局期 splitter 被动调整）；比例字段独立校验须 ∈ (0,1)，非法按字段缺失处理、不触发 DR-003；多标签页共享 settings.json，"最后一次调整生效"；hermes.md §11.4/§12 同步；Hermes.Tests 新增 13 例（比例换算/clamp/含布局往返/旧版兼容/非法布局不备份）；临时 harness 经 HermesTool 公开入口真实驱动 HermesPanel 验证——调整落盘、换窗口尺寸重开按比例还原、还原不改写文件、非法/旧版数据兼容，13 项断言全过（harness 验证后删除未入库）；全部测试 255 项全绿

## 1.3.0（2026-08-27）

- 第 14 步「工具 IoC 容器」完成，含用户授权的**破坏性契约变更**：`ITool` 改为 `RegisterServices(IServiceCollection)` + `CreateView(IToolHost, IServiceProvider)`（IFormatter 不变，全部第一方实现与 Hosting 测试桩同步适配）。Abstractions 新增 Microsoft.Extensions.DependencyInjection.Abstractions、App 新增 Microsoft.Extensions.DependencyInjection（版本入中央包管理，Abstractions 包列入插件 ALC SharedAssemblies 保证宿主/插件类型同一性）；App 组合根新增 ToolContainerRegistry——每工具独立 ServiceProvider，以实例形式预置 IToolHost 与按插件 id 打好 SourceContext 的 ILogger，单个工具注册/构建失败记日志+失败清单不中断其余，打开时按打开失败提示；生命周期约定落地——跨标签共享服务 singleton（Hermes 引擎/编排/工厂/各 Store，承接原懒加载字段的"浏览器会话"语义）、视图树 transient（CreateView 开 scope、面板 Disposed 释放 scope）、对话框不注册；HermesPanel/ProteusPanel 改构造注入（ILogger 直接注入即带插件上下文，替代 host.GetLogger 调用）；架构 §4/§6.0、hermes.md §4.1、proteus.md §4.1 同步；全部测试 241 项全绿；真实 App 启动冒烟（0 插件失败/0 容器失败）+ 独立冒烟工程经 PluginLoader ALC 加载部署产物验证：双"标签页"实例独立、HttpEngine 跨 scope 同实例、关闭后重开正常、回环真实发送 200、JSON 美化正确、日志含 hermes SourceContext

## 1.2.0（2026-08-27）

- 第 13 步「日志配置化」完成：程序目录 daedalus.json 支持日志级别配置（logging.default + logging.overrides 按插件 id 提级，文件缺失用默认 Information、JSON 损坏/级别不识记 Warning 并回退默认，解析先于 Serilog 初始化、警告在建好日志器后补记；新增 App 内部 LoggingBootstrap 承接解析与管道构建）；ToolHost.GetLogger 改用 SourceContext 承载插件 id（Serilog MinimumLevel.Override 按 SourceContext Ordinal 前缀匹配），规范 §7 新增插件禁止 ForContext<T>() 覆盖 SourceContext 的约束；关键路径补 Debug 日志——PluginLoader 扫描/逐 dll/发现实现/加载完成、ToolHost 数据目录分配与日志器创建、Hermes 变量替换始末/HTTP 逐跳（方法·URL·状态码·耗时）/后事件脚本执行始末/设置读写（加载来源：默认·文件·损坏恢复），Hermes 引擎/编排/设置 Store 以可选 ILogger 注入（现有测试构造不变）；架构 §6.2 重写、规范 §7 同步；全部测试 241 项全绿；临时控制台反射调用真实解析代码验证 override 行为（14 项断言全过）+ 真实 App 端到端验证配置生效

## 1.1.0（2026-08-27）

- 第 12 步「插件加载与外壳修复」完成：PluginAssemblyLoadContext 改非收集（isCollectible: false——项目无插件热卸载需求，可收集上下文存在被 GC 在使用中卸载的风险，是 Hermes 经 Jint 执行脚本时报"context 已 unload"的根因）；PluginCatalog 新增 LoadContexts 持有全部加载上下文引用备诊断；主窗口启动最大化；各插件部署目标补拷 .deps.json（AssemblyDependencyResolver 在部署目录走严格解析路径）；架构 §5.1 同步；全部测试 241 项全绿

## 1.0.1（2026-08-27）

- 新增构建脚本 `build.ps1` / `build.bat`（cmd 包装）：一键编译整个解决方案（src + plugins + tests），支持 Debug/Release 与 `-Clean`，构建后插件自动部署到 App 输出目录 plugins/

## 1.0.0（2026-08-27）

- 第 11 步「收尾」完成，首个全功能版本（重大里程碑）。本期交付：插件化工具箱外壳（Daedalus.App + Hosting——ITool/IFormatter 两级插件扫描加载、失败隔离与清单、IToolHost 宿主服务、Serilog 按天滚动日志、异常兜底）；Proteus 格式化工具（JSON/XML 第一方格式化器，格式化/压缩/校验含行列报错，FastColoredTextBox 高亮，设置持久化）；Hermes HTTP 客户端（请求编辑与发送、重定向跳转链每跳 tab、Cookie 共享与请求级覆盖、忽略证书校验开关、集合树 CRUD/拖拽、环境变量与 {{var}} 悬浮编辑、Postman/cURL 导入、Jint 后事件脚本沙箱 pm API、历史按天 jsonl 全量保存、30 天前按月归档 7z/zip、分层搜索与"搜索更久"）。全部测试 241 项全绿。本步内容：按需求文档逐条核对 FR/NFR（引用 step1~10 验证记录，纯鼠标交互路径如实标注"建议人工目视抽查"）；发布验证（exe + plugins/ + 空 data/logs 拷到干净目录可启动、插件全载、冷启动约 228ms），并修复验证发现的真实缺陷——Hermes 部署目标漏拷 Jint 4.4 拆分出的 Acornima.dll，部署形态下后事件脚本引擎不可用（补拷后干净目录 0 插件加载失败、ALC 冒烟脚本执行成功）；三份总文档与四份插件文档对照实现核对（修正架构 §6.1 数据目录树、Proteus 布局图补"复制输出"按钮），全部文档版本头去除"草案"

## 0.11.0（2026-08-27）

- 第 10 步「Hermes 历史归档与搜索」完成：HistoryArchive（30 天前日文件按月打包到 history/archive/yyyy-MM.7z|zip，面板启动后台检查 + 设置面板"立即归档"按钮；7z 探测依次 7z.exe/7za.exe、压缩 `7z a -mx=9`、校验 `7z t`，缺失时内置 zip SmallestSize 回退 + 回读条目校验；校验通过才删原文件，失败保留并清理半成品，已存在包的月份跳过不合并，FR-HERMES-053）；HistorySearch（原始 jsonl 行不区分大小写全文子串匹配；第一层直搜未压缩文件新→旧；"搜索更久"按月份新→旧逐包推进——zip 流式读取、7z 解压临时目录，每包刷新结果、可中途停止、结束清理临时文件，单包损坏跳过，FR-HERMES-054/055）；7z 调用收口为 ISevenZipRunner 抽象（取消时杀进程树避免占着临时目录），测试注入假桩覆盖 7z 路径；界面：历史区搜索框升级为 400ms 防抖全量真搜索（空关键词恢复最近列表）、直搜为空且存在归档包时出现"搜索更久/停止"按钮、归档记录可重放；Hermes.Tests 新增 20 例共 172 项；跨 6 个月样本自动化冒烟 12 项全过（真实 7z 进程端到端：后台归档→直搜→搜索更久→停止→重放→临时目录清理）；全部测试 241 项全绿

## 0.10.0（2026-08-27）

- 第 9 步「Hermes 导入与脚本」完成：PostmanImporter（结构嗅探 Collection v2.1 / Environment v1、嵌套文件夹/请求头/体/后事件脚本映射、form-data·graphql·auth·prerequest 等待忽略项汇总、v2.0 等版本明确拒绝、名称冲突追加序号，FR-HERMES-030~033）；CurlImporter（bash 分词——单双引号/转义/续行，-X·--url·-H·--data 系列·-b·-u 转 Basic·-A·-k 映射，未知参数汇总，导入到编辑区不入集合，FR-HERMES-034）；ScriptHost + PostmanApi（Jint 沙箱——每次新建 Engine、内存/超时取自设置、不开 AllowClr 只注入 pm，pm.environment.get/set/unset 与 pm.response.code/text/json/headers.get 子集，sendRequest/test/globals 抛"未实现"，异常隔离进结果不中断流程 FR-HERMES-043；脚本写操作结束后统一经 EnvironmentStore 立即持久化并刷新界面 FR-HERMES-044，只针对最终一跳执行 FR-HERMES-045）；界面接线：导入下拉菜单（Postman 文件 / cURL 粘贴）、响应区最终一跳"脚本输出"页；Jint 随插件部署到 plugins/；Hermes.Tests 新增 33 例共 152 项（样本文件驱动 TestData/ 5 个）；回环端到端冒烟 9 项全过（pm.environment.set → environments.json 落盘 + 面板环境数据源刷新 + 脚本输出页、Postman 样本导入落盘 + 集合树新增 + 脚本载入编辑区）；全部测试 221 项全绿

## 0.9.0（2026-08-27）

- 第 8 步「Hermes 界面」完成：Hermes 手工可用（除导入与脚本外）。Abstractions 新增可选契约 IToolCloseConfirmation，主窗口关标签页/关窗均咨询（FR-HERMES-012）；主面板 HermesPanel + 集合树 CollectionPanel（CRUD 右键菜单、拖拽移动）+ 请求编辑区 RequestEditorPanel（方法可编辑下拉、Params/Headers/Body[raw+urlencoded]/选项/后事件脚本五页、Ctrl+Enter 发送、脏标记）+ 响应区 ResponsePanel（跳转链每跳 tab、Content-Type 美化联动 FindFormatter）+ 历史列表 HistoryPanel（最近 7 天、过滤、重放）+ 环境切换下拉/管理窗口（变量密文列掩码）+ 设置面板（修改即保存，FR-HERMES-060/061）+ {{var}} 悬浮编辑（500ms 悬浮、就地改值立即持久化、secret 掩码切换、未定义就地创建，FR-HERMES-024）；非 UI 逻辑独立可测——SendOrchestrator（变量替换→引擎→历史组装）、RequestDraft（快照映射与脏比较）、QueryParamMapper（URL query ↔ Params 表）、ContentTypeFormatMapper/ResponseBeautifier、VariableReferenceFinder、RecentHistoryReader；Hermes.Tests 新增 44 例共 119 项；回环 TcpListener 端到端冒烟 15 项全过（建集合/配环境/发送美化/历史落盘/跳转链/Cookie 开关/设置持久化/悬浮弹窗），PluginLoader ALC CreateView 冒烟与 App 进程冒烟通过；全部测试 188 项全绿

## 0.8.0（2026-08-27）

- 第 7 步「Hermes HTTP 引擎」完成：HttpClientFactory（带/不带共享 CookieContainer 双 client 缓存、AllowAutoRedirect 恒 false、"忽略证书校验"开关变化销毁重建、internal 构造注入 handler 供测试，hermes.md §5.2）；HttpEngine（异步发送、取消、逐跳 Stopwatch 计时、重定向手动跟随——303 一律 GET 丢体 / 301·302 对 POST 按浏览器惯例改 GET / 307·308 保方法体重发、相对 Location 相对上一跳解析、Ordinal 精确 URL 环检测、10 跳上限标记，§5.3）；跳转链模型 SendRequest / ResponseHop(HopRequest+HopResponse) / SendResult（超限与环检测标记）；Hermes.Tests 新增 26 例——StubHandler 桩覆盖全部重定向行为与选项生效逻辑（全局/请求级覆盖双向）、回环 TcpListener 迷你 HTTP 服务器验证 Cookie 跨跳共享（避开 HttpListener 的 URL ACL 限制）；顺带修复 step6 的 IdGenerator 时间戳排序测试边界错误（第 10 字符含随机段，改为比较前 9 字符）；全部测试 144 项全绿

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
