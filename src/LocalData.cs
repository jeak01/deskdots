using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskDots {
    // Windows' built-in SQLite. Opens Codex databases with SQLITE_OPEN_READONLY.
    public sealed class ReadDb : IDisposable {
        IntPtr db;
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr stmt,IntPtr tail);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_name(IntPtr stmt,int col);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int col);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr stmt,int col,byte[] text,int len,IntPtr destroy);
        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s+"\0"); }
        static string Text(IntPtr p) { if(p==IntPtr.Zero)return "";int n=0;while(Marshal.ReadByte(p,n)!=0)n++;byte[] b=new byte[n];Marshal.Copy(p,b,0,n);return Encoding.UTF8.GetString(b); }
        public ReadDb(string path) { if(!File.Exists(path))throw new FileNotFoundException("找不到 Codex 数据库",path);int rc=sqlite3_open_v2(Utf8(path),out db,1,IntPtr.Zero);if(rc!=0){Dispose();throw new IOException("Codex 数据库暂时无法只读打开 ("+rc+")");}sqlite3_busy_timeout(db,1500); }
        public List<Dictionary<string,object>> Query(string sql,params string[] args) {
            IntPtr stmt;int rc=sqlite3_prepare_v2(db,Utf8(sql),-1,out stmt,IntPtr.Zero);if(rc!=0)throw new IOException("Codex 数据结构不兼容 ("+rc+")");
            try { for(int i=0;i<args.Length;i++){var b=Utf8(args[i]);sqlite3_bind_text(stmt,i+1,b,b.Length-1,new IntPtr(-1));}
                var rows=new List<Dictionary<string,object>>();int cols=sqlite3_column_count(stmt);
                while((rc=sqlite3_step(stmt))==100){var row=new Dictionary<string,object>();for(int i=0;i<cols;i++)row[Text(sqlite3_column_name(stmt,i))]=Text(sqlite3_column_text(stmt,i));rows.Add(row);}
                if(rc!=101)throw new IOException("Codex 数据库读取失败 ("+rc+")");return rows;
            } finally { sqlite3_finalize(stmt); }
        }
        public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
    }
    public static class LocalData {
        public static object State(Settings s) { return Json.Parse(Json.ReadShared(Path.Combine(s.CodexHome,".codex-global-state.json"))); }
        static string LatestDb(string dir,string pattern){return Directory.GetFiles(dir,pattern).OrderByDescending(p=>Json.Num(System.Text.RegularExpressions.Regex.Match(Path.GetFileName(p),@"\d+").Value)).FirstOrDefault();}
        public static LocalSnapshot Read(Settings s) {
            var result=new LocalSnapshot();object state=null;
            try { state=State(s);result.Dot=DotIdentity.FromState(state); } catch(Exception e){result.Error="Dot 状态未就绪："+e.Message;}
            try { result.Replies=Feed.Read(s,result.Dot); } catch(Exception e){result.FeedError="回复同步文件暂不可读："+e.Message;}
            try { result.Tasks=Tasks(s); } catch(Exception e){result.TaskError=e.Message;}
            // The app's own Activity cache contains only Dot-created tasks; account/user scoped.
            if(result.Dot.Valid)foreach(var cache in Json.Arr(Json.Get(Json.Get(state,"electron-persisted-atom-state"),"orbit-activity-snapshots-v1"))) {
                if(Json.S(cache,"accountId")!=result.Dot.AccountId || Json.S(cache,"userId")!=result.Dot.UserId || Json.S(cache,"tboId")!=result.Dot.Id)continue;
                foreach(var row in Json.Arr(Json.Get(cache,"data"))) {
                    var at=Json.Time(Json.Get(row,"updated_at"));bool running=Json.S(row,"status")=="in_progress";
                    if(!at.HasValue || (!running && at.Value<DateTime.UtcNow.AddDays(-3)))continue;
                    string id=Json.S(row,"thread_id");if(id=="" || result.Tasks.Any(t=>t.Id==id))continue;
                    string outcome=Json.S(row,"outcome");result.Tasks.Add(new TaskCard{Id=id,Title=Json.Clean(Json.S(row,"title")),Running=running,Status=running?"运行中（缓存）":outcome=="failed"?"失败":outcome=="interrupted"?"已中断":"已完成",Source="Dot 任务缓存",UpdatedAt=at.Value});
                }
            }
            result.Tasks=result.Tasks.OrderByDescending(t=>t.Running).ThenByDescending(t=>t.UpdatedAt).ToList();return result;
        }
        public static List<TaskCard> Tasks(Settings s) {
            var list=new List<TaskCard>();if(!Directory.Exists(s.CodexHome))throw new IOException("请在设置中选择 Codex 数据目录");
            var state=LatestDb(s.CodexHome,"state_*.sqlite");if(state==null)throw new IOException("尚未找到本机 Codex 任务");
            var history=LatestDb(s.CodexHome,"thread_history_*.sqlite");
            using(var db=new ReadDb(state)) using(var h=history==null?null:new ReadDb(history)) {
                var cols=new HashSet<string>(db.Query("PRAGMA table_info(threads)").Select(r=>Json.S(r,"name")));
                // Do not read first_user_message, preview, item_json or any chat body.
                var sql="SELECT id,"+(cols.Contains("name")?"COALESCE(NULLIF(name,''),title)":"title")+" AS title,updated_at,source FROM threads WHERE archived=0 AND source IN ('cli','vscode','appServer','exec') ORDER BY updated_at DESC";
                foreach(var row in db.Query(sql)) {
                    var at=Json.Time(Json.Get(row,"updated_at"))??DateTime.MinValue;
                    object turn=null;if(h!=null)turn=h.Query("SELECT status,completed_at FROM thread_turns WHERE thread_id=? ORDER BY rollout_ordinal DESC LIMIT 1",Json.S(row,"id")).FirstOrDefault();
                    var status=Json.S(turn,"status");bool running=status=="inProgress";
                    if(!running && at<DateTime.UtcNow.AddDays(-3))continue;
                    list.Add(new TaskCard { Id=Json.S(row,"id"),Title=Json.Clean(Json.S(row,"title"),80),UpdatedAt=at,Running=running,Source="Codex · 本机记录",Status=running?"进行中（记录）":status=="completed"?"已完成":status=="failed"?"失败":status=="interrupted"?"已中断":"状态未知" });
                }
            }return list;
        }
    }
    public static class Feed {
        public static string PathName(Settings s){return Path.Combine(s.DataDir,"dot-replies.json");}
        public static List<DotReply> Read(Settings s,DotIdentity dot) {
            if(!dot.Valid || !File.Exists(PathName(s)))return new List<DotReply>();
            return Parse(Json.Parse(Json.ReadShared(PathName(s))),dot);
        }
        public static List<DotReply> Parse(object feed,DotIdentity dot) {
            if(!dot.Valid || Json.S(feed,"accountId")!=dot.AccountId || Json.S(feed,"userId")!=dot.UserId || Json.S(feed,"dotId")!=dot.Id)return new List<DotReply>();
            var rows=new List<DotReply>();
            foreach(var row in Json.Arr(Json.Get(feed,"replies"))) {
                if(Json.S(row,"role")!="assistant" || Json.S(row,"source")!="dot" || Json.S(row,"dotThreadId")!=dot.ThreadId)continue;
                var at=Json.Time(Json.Get(row,"createdAt"));var text=Json.S(row,"text");var id=Json.S(row,"id");
                if(id=="" || string.IsNullOrWhiteSpace(text) || !at.HasValue)continue;
                rows.Add(new DotReply {Id=id,Text=text.Length>24000?text.Substring(0,24000):text,At=at.Value});
            }
            return rows.GroupBy(r=>r.Id).Select(g=>g.Last()).OrderByDescending(r=>r.At).Take(30).ToList();
        }
        public static object Publish(Settings s,object input) {
            var dot=DotIdentity.FromState(LocalData.State(s));if(!dot.Valid)throw new InvalidOperationException("未找到当前已登录的 Dot，请先在主应用选择 Dot");
            if(Json.S(input,"dotThreadId")!=dot.ThreadId)throw new InvalidOperationException("只接受当前 Dot 线程的回复，普通聊天不能发布");
            string text=Json.S(input,"text");if(string.IsNullOrWhiteSpace(text)||text.Length>24000)throw new ArgumentException("text 长度必须为 1–24000 字符");
            string id=Json.S(input,"messageId");if(id.Length<1 || id.Length>256)throw new ArgumentException("需要稳定的 messageId，以便去重");
            using(var mutex=new System.Threading.Mutex(false,"Local\\DeskDotsFeed-"+StableHash(s.DataDir))) {
                bool held=false;try {try {held=mutex.WaitOne(5000);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException("同步通道繁忙，请重试");
                    var replies=Read(s,dot);replies.RemoveAll(r=>r.Id==id);replies.Insert(0,new DotReply{Id=id,Text=text,At=DateTime.UtcNow});
                    var rows=replies.OrderByDescending(r=>r.At).Take(50).Select(r=>Json.D("id",r.Id,"role","assistant","source","dot","dotThreadId",dot.ThreadId,"text",r.Text,"createdAt",r.At.ToString("o"))).ToArray();
                    Json.AtomicWrite(PathName(s),Json.D("schemaVersion",1,"accountId",dot.AccountId,"userId",dot.UserId,"dotId",dot.Id,"replies",rows));
                    return Json.D("saved",true,"messageId",id);
                } finally{if(held)mutex.ReleaseMutex();}
            }
        }
        static string StableHash(string text) {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Replace("-","").Substring(0,24);}
    }
}
