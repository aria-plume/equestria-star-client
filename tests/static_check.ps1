$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$required = @(
  "project.godot",
  "EquestriaStar.csproj",
  "scenes/app/App.tscn",
  "scenes/screens/LoginScreen.tscn",
  "scenes/screens/RegisterScreen.tscn",
  "scenes/screens/LobbyScreen.tscn",
  "scenes/screens/RoomScreen.tscn",
  "scenes/screens/CharacterSelectScreen.tscn",
  "scenes/screens/GameScreen.tscn",
  "scenes/screens/GameTestScreen.tscn",
  "scenes/components/RoomCard.tscn",
  "scenes/components/RoomMemberRow.tscn",
  "scenes/components/CharacterCardView.tscn",
  "scenes/components/CreateRoomDialog.tscn",
  "scenes/game/map/HexCell3D.tscn",
  "scenes/game/map/HexCellDemo.tscn",
  "scenes/game/map/GameMapScene.tscn",
  "scenes/game/ui/GameHud.tscn",
  "scenes/game/ui/GameHudDemo.tscn",
  "scenes/game/ui/OtherPlayerHudEntry.tscn",
  "assets/characters/cards/骰子.png",
  "assets/characters/cards/大布莱恩.jpg",
  "assets/characters/cards/橙羽.jpg",
  "assets/characters/cards/循雨.jpg",
  "assets/characters/cards/hf.jpg",
  "assets/characters/cards/蛋黄.jpg",
  "assets/characters/cards/浊星.jpg",
  "assets/characters/cards/正负等式.jpg",
  "assets/characters/cards/夜灵.jpg",
  "assets/characters/cards/丹青.jpg",
  "assets/characters/cards/角色卡背.jpg",
  "scripts/api/ApiConfig.cs",
  "scripts/api/ApiClient.cs",
  "scripts/api/ApiDtos.cs",
  "scripts/state/Session.cs",
  "scripts/state/ActiveSessionModel.cs",
  "scripts/state/RoomListModel.cs",
  "scripts/state/RoomStateModel.cs",
  "scripts/state/CharacterSelectionModel.cs",
  "scripts/game/map/HexCellData.cs",
  "scripts/game/map/HexCellMeshFactory.cs",
  "scripts/game/map/HexCell3D.cs",
  "scripts/game/map/HexCellDemo.cs",
  "scripts/game/map/GameMapScene.cs",
  "scripts/game/map/GameMapBuilder.cs",
  "scripts/game/map/HexCoordinateConverter.cs",
  "scripts/game/map/MapLayoutConfig.cs",
  "scripts/game/map/MapDataValidator.cs",
  "scripts/game/map/MapCameraFitter.cs",
  "scripts/game/map/MapCameraController.cs",
  "scripts/game/ui/GameHud.cs",
  "scripts/game/ui/GameHudDemo.cs",
  "scripts/game/ui/OtherPlayerHudEntry.cs",
  "scripts/ui/App.cs",
  "scripts/ui/LoginScreen.cs",
  "scripts/ui/RegisterScreen.cs",
  "scripts/ui/LobbyScreen.cs",
  "scripts/ui/RoomCard.cs",
  "scripts/ui/CreateRoomDialog.cs",
  "scripts/ui/RoomScreen.cs",
  "scripts/ui/RoomMemberRow.cs",
  "scripts/ui/CharacterSelectScreen.cs",
  "scripts/ui/CharacterCardView.cs",
  "scripts/ui/GameScreen.cs",
  "scripts/ui/GameTestScreen.cs",
  "tests/RunUnitTests.cs",
  "tests/RunRoomScreenTests.cs",
  "tests/RunCharacterSelectTests.cs",
  "tests/RunActiveSessionTests.cs",
  "tests/RunHexCellTests.cs",
  "tests/RunGameMapTests.cs",
  "tests/RunGameScreenTests.cs",
  "tests/RunMapSmokeTest.cs"
)

foreach ($path in $required) {
  if (-not (Test-Path -LiteralPath $path)) {
    throw "缺少必要文件：$path"
  }
}

$backendRefs = Get-ChildItem -Path scripts,scenes -Recurse -Include *.cs,*.tscn |
  Select-String -Pattern "182\.92\.234\.193|/api/v1" |
  Where-Object { $_.Path -notlike "*scripts\api\ApiConfig.cs" }
if ($backendRefs) {
  throw "Backend URL or API prefix must only appear in ApiConfig."
}

$passwordStorageRefs = Get-ChildItem -Path scripts -Recurse -Include *.cs |
  Select-String -Pattern 'password.*save|save.*password|Base64|base64|fixed key'
if ($passwordStorageRefs) {
  throw "Detected suspicious password persistence or weak obfuscation."
}

$teachingFiles = Get-ChildItem -Path . -Recurse -File |
  Where-Object { $_.Name -match "教程|教学|tutorial|lesson" -and $_.Extension -ne ".md" }
if ($teachingFiles) {
  throw "Detected unexpected teaching/tutorial files."
}

$legacyRoomTestRefs = Get-ChildItem -Path scripts,scenes -Recurse -Include *.cs,*.tscn |
  Select-String -Pattern 'RoomTestScreen|EnterRoomTest|ShowRoomTest'
if ($legacyRoomTestRefs) {
  throw "Detected legacy room test screen semantics."
}

$legacyCharacterSelectRefs = Get-ChildItem -Path scripts,scenes -Recurse -Include *.cs,*.tscn |
  Select-String -Pattern 'CharacterSelectTestScreen|ShowCharacterSelectTest|EnterCharacterSelectTest'
if ($legacyCharacterSelectRefs) {
  throw "Detected legacy character selection test screen semantics."
}

$legacyGameFlowRefs = Get-ChildItem -Path scripts -Recurse -Include *.cs |
  Where-Object { $_.FullName -notlike "*scripts\ui\GameTestScreen.cs" } |
  Select-String -Pattern 'GameTest|EnterGameTest|ShowGameTest'
if ($legacyGameFlowRefs) {
  throw "Detected legacy game test screen references in normal flow."
}

$removedStartEndpoint = Get-ChildItem -Path scripts -Recurse -Include *.cs |
  Select-String -Pattern '/rooms/\{roomId\}/start["\)]'
if ($removedStartEndpoint) {
  throw "Detected removed room start endpoint."
}

Write-Host "Static project check passed."
