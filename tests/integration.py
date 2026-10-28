"""Offline native-executable tests. All data is synthetic and workspace-local."""
import datetime as dt
import json
import pathlib
import shutil
import sqlite3
import subprocess
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = ROOT / "dist" / "DeskDots.exe"
WORK = ROOT / "tests" / "tmp" / str(uuid.uuid4())
WORK.mkdir(parents=True)
HOME = WORK / "codex"
DATA = WORK / "data"
HOME.mkdir()
DATA.mkdir()

def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")

try:
    identity = {"accountId": "test-account", "userId": "test-user", "response": {"selection": {"thread_id": "dot-thread"}, "profile": {"id": "test-dot", "active_root_thread_id": "dot-thread", "display_name": "Test Dot"}}}
    write(HOME / ".codex-global-state.json", {"electron-persisted-atom-state": {"primary-aeon-selection-v1": identity}})
    write(DATA / "settings.json", {"codexHome": str(HOME), "codexExe": "missing-test-exe"})
    db = sqlite3.connect(HOME / "state_5.sqlite")
    db.execute("CREATE TABLE threads(id TEXT, name TEXT, title TEXT, updated_at INTEGER, source TEXT, archived INTEGER)")
    h = sqlite3.connect(HOME / "thread_history_1.sqlite")
    h.execute("CREATE TABLE thread_turns(thread_id TEXT, status TEXT, completed_at INTEGER, rollout_ordinal INTEGER)")
    now = int(dt.datetime.now(dt.timezone.utc).timestamp())
    fixtures = [("recent", now - 2*86400, "completed", "vscode", 0), ("old", now - 4*86400, "completed", "vscode", 0), ("running-old", now - 7*86400, "inProgress", "vscode", 0), ("recent-failed", now - 60, "failed", "cli", 0), ("archived", now, "completed", "vscode", 1), ("subagent", now, "completed", "subAgent", 0)]
    for tid, at, status, source, archived in fixtures:
        db.execute("INSERT INTO threads VALUES(?,?,?,?,?,?)", (tid, None, tid, at, source, archived))
        h.execute("INSERT INTO thread_turns VALUES(?,?,?,1)", (tid, status, None))
    db.commit();h.commit();db.close();h.close()
    p = subprocess.run([str(EXE), "--data-dir", str(DATA), "--diagnose", str(WORK / "diagnose.json"), "--local-only"], timeout=20)
    assert p.returncode == 0
    diagnostic = json.loads((WORK / "diagnose.json").read_text(encoding="utf-8"))
    assert diagnostic["taskCount"] == 3, diagnostic
    assert diagnostic["runningCount"] == 1, diagnostic
    assert diagnostic["taskError"] == "", diagnostic
    print("PASS: 72-hour filter, old ongoing tasks, archived/subagent exclusion, native SQLite reader")
    requests = [
        {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "fixture", "version": "1"}}},
        {"jsonrpc": "2.0", "method": "notifications/initialized"},
        {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
        {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "deskdots_context", "arguments": {}}},
        {"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {"name": "deskdots_publish_dot_reply", "arguments": {"dotThreadId": "dot-thread", "messageId": "m1", "text": "Dot 的测试回复"}}},
        {"jsonrpc": "2.0", "id": 5, "method": "tools/call", "params": {"name": "deskdots_publish_dot_reply", "arguments": {"dotThreadId": "ordinary", "messageId": "forbidden", "text": "不能出现的普通聊天"}}},
    ]
    p = subprocess.run([str(EXE), "--data-dir", str(DATA), "--mcp"], input="\n".join(json.dumps(r) for r in requests)+"\n", text=True, encoding="utf-8", capture_output=True, timeout=15)
    assert p.returncode == 0, p.stderr
    responses = {r["id"]:r for r in map(json.loads,p.stdout.splitlines())}
    assert set(responses) == {1,2,3,4,5}, responses
    assert len(responses[2]["result"]["tools"]) == 2
    assert responses[4]["result"]["content"]
    assert "error" in responses[5], responses[5]
    feed = json.loads((DATA / "dot-replies.json").read_text(encoding="utf-8"))
    assert len(feed["replies"]) == 1 and feed["replies"][0]["text"] == "Dot 的测试回复"
    assert feed["accountId"] == "test-account"
    print("PASS: stdio MCP handshake, tool discovery, publication, ordinary-thread rejection, UTF-8 roundtrip")
finally:
    # Restrict recursive cleanup to the exact fixture directory created above.
    assert WORK.parent == ROOT / "tests" / "tmp" and WORK.name.count("-") == 4
    shutil.rmtree(WORK)
