$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$required = @(
  "project.godot",
  "EquestriaStar.csproj",
  "AGENTS.md",
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
  "scenes/game/map/PlayerToken3D.tscn",
  "scenes/game/map/visuals/origami/town-hall/OrigamiCellVisual.tscn",
  "scenes/game/map/visuals/origami/everfree/OrigamiCellVisual.tscn",
  "scenes/game/ui/GameHud.tscn",
  "scenes/game/ui/CardViewerOverlay.tscn",
  "scenes/game/ui/InventoryItemCard.tscn",
  "scenes/game/ui/QuestJournalOverlay.tscn",
  "scenes/game/ui/QuestTargetSubmissionOverlay.tscn",
  "scenes/game/ui/QuestTaskCard.tscn",
  "scenes/game/ui/GameHudDemo.tscn",
  "scenes/game/ui/OtherPlayerHudEntry.tscn",
  "scenes/game/dice/Dice3D.tscn",
  "scenes/game/dice/DiceTray.tscn",
  "scenes/game/dice/DiceDemo.tscn",
  "scenes/game/dice/WorkDiceOverlay.tscn",
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
  "scripts/game/map/OfficialMapStaticVisualLoader.cs",
  "scripts/game/map/StaticMapVisualContract.cs",
  "scripts/game/map/StaticMapInteractionBuilder.cs",
  "scripts/game/map/GameMapBuilder.cs",
  "scripts/game/map/OrigamiVisualCatalog.cs",
  "scripts/game/map/EnvironmentArtCatalog.cs",
  "scripts/game/map/MapEnvironmentBuilder.cs",
  "scripts/game/map/HexCoordinateConverter.cs",
  "scripts/game/map/MapLayoutConfig.cs",
  "scripts/game/map/MapDataValidator.cs",
  "scripts/game/map/MapCameraFitter.cs",
  "scripts/game/map/MapCameraController.cs",
  "scripts/game/map/visual_runtime/MapVisualRuntime.cs",
  "scripts/game/map/visual_runtime/MapVisualP9Profile.cs",
  "scripts/game/map/visual_runtime/MapVisualEnvironmentMotion.cs",
  "scripts/game/map/visual_runtime/PaperAtmosphere.cs",
  "scenes/game/map/visual_runtime/MapVisualRuntime.tscn",
  "scripts/game/map/PlayerToken3D.cs",
  "scripts/game/map/PlayerTokenLayout.cs",
  "scripts/game/map/PlayerTokenManager.cs",
  "scripts/game/map/PlayerMoveState.cs",
  "scripts/game/connection/GameConnectionLease.cs",
  "scripts/game/ui/GameHud.cs",
  "scripts/game/ui/CardArtCatalog.cs",
  "scripts/game/ui/CardViewerOverlay.cs",
  "scripts/game/ui/InventoryItemCard.cs",
  "scripts/game/ui/ItemSystemModel.cs",
  "scripts/game/ui/QuestJournalOverlay.cs",
  "scripts/game/ui/QuestTaskCard.cs",
  "scripts/game/ui/QuestSystemModel.cs",
  "scripts/game/ui/QuestTargetSubmissionModel.cs",
  "scripts/game/ui/GameHudDemo.cs",
  "scripts/game/ui/OtherPlayerHudEntry.cs",
  "scripts/game/dice/DiceMeshFactory.cs",
  "scripts/game/dice/Dice3D.cs",
  "scripts/game/dice/DiceTray.cs",
  "scripts/game/dice/DiceRollController.cs",
  "scripts/game/dice/DiceDemo.cs",
  "scripts/game/dice/WorkDiceOverlay.cs",
  "assets/art/mappings/origami-cell-visuals.json",
  "assets/art/mappings/map-environment.json",
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
  "scripts/ui/GameScreen.Items.cs",
  "scripts/ui/GameScreen.Quests.cs",
  "scripts/ui/GameScreen.QuestChecks.cs",
  "scripts/ui/GameScreen.QuestTargetSubmissions.cs",
  "scripts/game/ui/QuestCheckModel.cs",
  "scripts/game/ui/QuestCheckOverlay.cs",
  "scenes/game/ui/QuestCheckOverlay.tscn",
  "scripts/ui/GameTestScreen.cs",
  "tests/RunUnitTests.cs",
  "tests/RunRoomScreenTests.cs",
  "tests/RunCharacterSelectTests.cs",
  "tests/RunActiveSessionTests.cs",
  "tests/RunHexCellTests.cs",
  "tests/RunGameMapTests.cs",
  "tests/RunGameScreenTests.cs",
  "tests/RunGameConnectionTests.cs",
  "tests/RunCore9CardViewerTests.cs",
  "tests/RunPlayerTokenTests.cs",
  "tests/RunPlayerMoveTests.cs",
  "tests/RunMovementV2Tests.cs",
  "tests/RunQuestSystemTests.cs",
  "tests/RunMapSmokeTest.cs",
  "tests/RunDiceTests.cs",
  "tests/RunWorkFlowTests.cs",
  "tests/RunWorkAttributeSelectionTests.cs",
  "tests/RunQuestCheckTests.cs",
  "tests/RunQuestTargetSubmissionTests.cs",
  "tests/RunStaticMapBakeTests.cs",
  "scenes/internal/map_baking/Pr1MapBakingStage.tscn",
  "scenes/internal/map_baking/StaticMapPckLoader.tscn",
  "assets/maps/official/OfficialMapVisual.tscn",
  "assets/maps/official/OfficialMapVisual.metadata.json",
  "artifacts/static-map/official-map-visual.pck"
)

foreach ($path in $required) {
  if (-not (Test-Path -LiteralPath $path)) {
    throw "缺少必要文件：$path"
  }
}

$apiDtos = Get-Content -Raw -Encoding UTF8 "scripts/api/ApiDtos.cs"
if ($apiDtos -notmatch '\[JsonPropertyName\("requestId"\)\]\s*public string RequestId') {
  throw "GameActionRequestDto must serialize a top-level requestId."
}

$gameScreenSource = (Get-ChildItem -LiteralPath "scripts/ui" -Filter "GameScreen*.cs" |
  ForEach-Object { Get-Content -Raw -Encoding UTF8 $_.FullName }) -join "`n"
$actionRequestIdAssignments = [regex]::Matches(
  $gameScreenSource,
  'RequestId\s*=\s*CreateGameActionRequestId\(\)'
).Count
$workRequestFactoryAssignments = [regex]::Matches(
  $gameScreenSource,
  'WorkAttributeSelectionModel\.CreateWorkRequest\([\s\S]{0,300}?CreateGameActionRequestId\(\)'
).Count
$itemRequestFactoryAssignments = [regex]::Matches(
  $gameScreenSource,
  'ItemSystemModel\.(CreateShopBuyRequest|CreateItemRequest)\([\s\S]{0,400}?CreateGameActionRequestId\(\)'
).Count
if (($actionRequestIdAssignments + $workRequestFactoryAssignments + $itemRequestFactoryAssignments) -ne 14) {
  throw "Each move, turn, quest, reward, discard, work, check and item operation must create one requestId per logical action."
}

if ($gameScreenSource -match 'PLAY_ITEM') {
  throw "Item actions must use USE_ITEM exactly once and must not submit legacy PLAY_ITEM."
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

$forbiddenExtractedArtFiles = Get-ChildItem -Path assets/art -Recurse -File |
  Where-Object {
    $_.Extension -in ".cs", ".gd", ".gdshader", ".tscn", ".tres", ".ps1", ".cmd" -or
    $_.Name -in "catalog.json", "token-layout.json", "map-presentation.json", "zoom-detail.json"
  }
if ($forbiddenExtractedArtFiles) {
  throw "Detected non-art files copied into assets/art."
}

$staticMapScene = Get-Content -LiteralPath "assets/maps/official/OfficialMapVisual.tscn" -Raw -Encoding UTF8
if ($staticMapScene -match 'type="Script"|script\s*=|type="Area3D"|type="CollisionShape3D"|TokenRoot|InteractionArea|HighlightMesh') {
  throw "Static map contains runtime scripts, input, collision, highlight or token nodes."
}

$projectConfig = Get-Content -LiteralPath "project.godot" -Raw -Encoding UTF8
if ($projectConfig -notmatch 'renderer/rendering_method="forward_plus"' -or
    $projectConfig -notmatch 'renderer/rendering_method\.mobile="mobile"') {
  throw "Project renderer must default to Forward+ with a Mobile override."
}

$runtimeScene = Get-Content -LiteralPath "scenes/game/map/visual_runtime/MapVisualRuntime.tscn" -Raw -Encoding UTF8
if ($runtimeScene -match 'blocks_map_input') {
  throw "MapVisualRuntime post-processing must not block map input."
}

$formalMapSource = Get-Content -LiteralPath "scripts/game/map/GameMapScene.cs" -Raw -Encoding UTF8
if ($formalMapSource -match 'GameMapBuilder|OrigamiVisualCatalog|MapEnvironmentBuilder') {
  throw "Formal GameMapScene must not use legacy visual builders."
}

$formalMapScene = Get-Content -LiteralPath "scenes/game/map/GameMapScene.tscn" -Raw -Encoding UTF8
if ($formalMapScene -match 'MapEnvironmentRoot|\[node name="Floor"|\[node name="Environment"|\[node name="DirectionalLight3D"') {
  throw "Formal GameMapScene must not duplicate legacy floor, environment or lighting."
}
if ($formalMapScene -notmatch '\[node name="StaticVisualRoot"' -or
    $formalMapScene -notmatch '\[node name="InteractionRoot"' -or
    $formalMapScene -notmatch 'MapVisualRuntime.tscn') {
  throw "Formal GameMapScene must contain static visual, interaction and runtime layers."
}

$pckForbiddenSource = Get-ChildItem -Path scripts/internal/map_baking -Recurse -Include *.cs |
  Select-String -Pattern 'ApiClient|Session\.Instance|/api/v1|HTTP(Request|Client)'
if ($pckForbiddenSource) {
  throw "Internal static map baker must not depend on client network/session services."
}

Write-Host "Static project check passed."
