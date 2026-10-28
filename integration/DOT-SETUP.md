# 启用 Dot 回复同步

DeskDots 的额度和本机任务自动读取，Dot 的聊天正文需要启用本机同步。当前公开接口没有提供 Dot 消息读取能力；应用不会提取账号 token、调用未公开的云端聊天接口或用普通聊天代替 Dot 的回复。

1. 双击 DeskDots.exe，在右上角设置中点击「复制同步说明，发给 Dot 一次」。
2. 在你自己的 Dot 会话里粘贴、发送，让 Dot 通过已连接的 Windows 电脑执行说明中的发布命令。
3. Dot 发布后，组件会在 8 秒内读取。后续回复是否持续同步取决于 Dot 是否执行同步指令；这不是自动订阅云端消息。

如果 Dot 无法访问本机文件或运行命令，需要先在 Dot 的 Computers 设置中连接这台电脑。这一步由用户在主应用完成。

同步文件只接受当前 Dot 线程、assistant 角色、dot 来源，并按账号和用户隔离。更换 Dot 或账号后旧消息不会混入。线程 ID 检查可防止误发布，不是对消息作者的密码学证明；仅连接你信任的本机客户端。

## 可选：MCP 通道

组件第一次运行后会在自身数据目录生成 `mcp-config.toml`，内含当前机器的完整 exe 路径。可以将该节加入 Codex 的 MCP 配置，以 `DeskDots.exe --mcp --data-dir <目录>` 启动 stdio 服务。不要覆盖现有 config.toml。

工具 `deskdots_context` 返回当前 Dot 线程 ID；`deskdots_publish_dot_reply` 接受：

```json
{"dotThreadId":"当前 Dot 线程 ID","messageId":"回复的稳定唯一 ID","text":"Dot 正式回复正文"}
```

也可把同样的 JSON 写入 UTF-8 文件，然后运行：

```powershell
& '.\DeskDots.exe' --publish-file '.\dot-outbox.json'
```

如组件使用自定义数据目录，发布命令和 MCP 必须使用同一个 `--data-dir`。

只同步 Dot 的正式回复，禁止同步普通 Codex 聊天、用户消息、工具结果、内部推理和其他 agent 输出。重复 messageId 会替换同一条消息。最新 30 条显示在组件中，发布时保留最多 50 条。
