# 项目长期开发规范

- 项目使用 Godot 4.6.2 Mono、C# 和 .NET 8。
- Godot 可执行文件路径为 `C:\Users\Ariaplume\OneDrive\Desktop\快捷键\开发\Godot_v4.6.2-stable_win64.exe`；C# 构建和无界面验证应使用支持 Mono/.NET 的 Godot 4.6.2 可执行文件。
- 所有文件保持 UTF-8 编码，代码注释使用中文。
- PowerShell 读取中文文件前设置 UTF-8 输出，并使用 `Get-Content -Encoding UTF8`。
- 修改前先检查现有场景、代码、测试，并从 `docs/接口交接/README.md` 阅读最新接口交接，确认当前架构与契约；不再从根目录寻找旧版接口文件。
- 接口、玩法接入及策划交接文档统一存放在 `docs/接口交接/`。新增或修改契约时同步该目录索引、主文档及相关专项，明确已部署、待开发和历史资料的区别；不在根目录复制多份契约。
- 优先复用已有场景结构、DTO、`ApiClient`、Session 和状态模型，不重复实现已有能力。
- 不修改或删除无关文件，不重置、覆盖或清理用户已有修改。
- 客户端以服务端状态为权威，不自行伪造业务结果或绕过服务端校验。
- UI 与地图输入继续遵守 `blocks_map_input` Group 的语义路由规则；纯布局节点不得无意阻塞地图输入。
- 修改后执行 `dotnet build`、`tests/static_check.ps1`、相关自动化测试和 Godot headless 检查。
- 涉及 3D 场景时必须实际运行，并完成视觉与交互检查。
- 完成后报告修改文件、实现方式、测试结果及未覆盖范围。
