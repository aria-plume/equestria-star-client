# 小马国之星客户端

Godot **4.6.2 .NET** / C# / .NET 8。当前入口为 `scenes/app/App.tscn`。

## 折纸美术接入

已安装43组当前交付、115个独立GLB组件。正式地图按后端地标名称及明确别名匹配，不改变格子ID、坐标、区域偏移、行动协议或连通关系。

- [美术接入与交接说明](docs/art/INTEGRATION.md)
- [素材清单与地图匹配结果](docs/art/ASSETS.md)
- [本轮实际地图截图](docs/art/map.png)
- 离线素材预览：在 Godot 打开 `scenes/art/OrigamiGallery.tscn` 并运行当前场景；支持全库、地图快照和单项查看。
- 在线客户端：运行主场景。账号和后端设置沿用现有项目。

## 本地验证

```powershell
dotnet build
./tests/static_check.ps1
python tools/art/validate_assets.py --source-root 'I:/blender文件/小马国之心'
godot --headless --path . --script res://tests/RunOrigamiArtTests.cs
godot --headless --path . --script res://tests/RunHexCellTests.cs
godot --headless --path . --script res://tests/RunGameMapTests.cs
```

`godot` 代表对应的 Godot .NET 可执行文件。首次克隆后先用编辑器打开项目，完成资源导入与C#构建。Windows导出预设已包含资产目录JSON，发布前仍需安装匹配的导出模板并执行打包验证。

## 接口交接资料

仓库根目录的8份既有接口/交接文档全部保留。旧版“重构需求”包含历史流程；现行房间选角、准备和自动开局以 `ES完整API文档.md`、`API接口清单.md` 与当前代码为准。中文展示使用 `xxxText`，逻辑使用原协议字段。
