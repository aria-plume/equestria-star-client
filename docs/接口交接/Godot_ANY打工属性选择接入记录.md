# Godot ANY 打工属性选择接入记录

更新时间：2026-11-06。

## 已确认契约

- `GET /api/v1/games/{gameId}/work/current?playerId={playerId}` 返回固定六维属性时，客户端沿用原准备流程。
- 返回 `attributeType=ANY` 时，客户端必须让玩家明确选择 `STRENGTH`、`SPEED`、`SOCIAL`、`CHARM`、`MAGIC`、`MIND` 之一。
- `POST /api/v1/games/{gameId}/actions` 的 `WORK` payload 只包含所选具体 `attributeType`；`ANY` 不是合法最终选择。
- 取消选择不提交行动、不创建判定，也不调用取消判定接口。
- 准备成功且取得匹配的 `CHECK_ROLL_PENDING` 后，继续复用既有独立物理投骰、重投授权和最终确认流程。

## 客户端实现

- `WorkDiceOverlay` 增加独立属性选择态和提交态；选择遮罩继续使用 `blocks_map_input`，背景和 Esc 不提供关闭入口。
- 六个按钮显示中文，信号和请求始终携带稳定英文枚举；没有默认属性，也不按玩家数值自动选择。
- 玩家点击属性后，`GameScreen` 重新同步 player-view、available-actions，并重新读取 work/current；只有连接、本人回合、WORK资格、玩家身份和最新版本仍有效，且配置仍为 ANY 时才创建本次 requestId 并提交。
- 快速重复点击由选择状态、按钮禁用和现有写操作互斥共同阻止。场景退出、身份切换、选择取消或提交前资格失效会释放选择状态；已发送请求的未知结果继续沿用原待判定恢复边界，不用新 requestId 自动重发。
- 固定属性工作仍不显示选择层；未知属性明确报错，不回退到任意六维。

## 本地验证边界

- `RunWorkAttributeSelectionTests` 使用正常 Viewport GUI 鼠标分发验证六个按钮、取消、快速重复点击、输入遮罩和请求 JSON 工厂；模拟结果不是公网真实服务联调。
- 原 `RunWorkFlowTests` 的完整 GameScreen 流程仍受既有启动等待未结束问题影响，本轮不把该独立问题误报为通过。
- 未登录或操作真实对局，ANY 工作的公网准备、物理投骰、重投和最终结算仍由用户手动验证。

需要用户决策的点：本轮未发现。
