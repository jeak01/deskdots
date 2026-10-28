using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DeskDots {
    public static class Json {
        public static object Parse(string value) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }.DeserializeObject(value); }
        public static string Stringify(object value) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(value); }
        public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object value, string key) { object result; return Obj(value).TryGetValue(key, out result) ? result : null; }
        public static string Str(object value) { return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public static string S(object value, string key) { return Str(Get(value, key)); }
        public static double Num(object value, double fallback = 0) { double n; return double.TryParse(Str(value), NumberStyles.Any, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
        public static IEnumerable<object> Arr(object value) { return value as object[] ?? new object[0]; }
        public static DateTime? Time(object value) {
            double number = Num(value, double.NaN);
            if (!double.IsNaN(number)) { try { return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(number > 100000000000 ? number : number * 1000); } catch { return null; } }
            DateTime date;
            return DateTime.TryParse(Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date) ? date.ToUniversalTime() : (DateTime?)null;
        }
        public static string Clean(string value, int max = 180) { value = Regex.Replace(value ?? "", @"\s+", " ").Trim(); return value.Length > max ? value.Substring(0,max) + "…" : value; }
        public static string ReadShared(string path) { using (var s = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete)) using (var r = new StreamReader(s)) return r.ReadToEnd(); }
        public static void AtomicWrite(string path, object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(tmp, Stringify(value), new System.Text.UTF8Encoding(false)); if (File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        public static Dictionary<string, object> D(params object[] entries) { var d = new Dictionary<string,object>(); for (int i=0;i<entries.Length;i+=2) d[(string)entries[i]]=entries[i+1]; return d; }
    }
    public sealed class Settings {
        public string CodexHome, CodexExe, DataDir;
        public bool Pinned = false;
        public int CompactLayout;
        public double Left = -1, Top = -1;
        public string PathName { get { return Path.Combine(DataDir,"settings.json"); } }
        public static Settings Load(string dataDir) {
            var s = new Settings { DataDir = dataDir };
            object o = null; try { if (File.Exists(s.PathName)) o = Json.Parse(File.ReadAllText(s.PathName)); else { var bootstrap=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeskDots.connection.json");if(File.Exists(bootstrap))o=Json.Parse(File.ReadAllText(bootstrap)); } } catch { }
            s.CodexHome = Json.S(o,"codexHome");
            if (s.CodexHome == "") s.CodexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Environment.GetEnvironmentVariable("CODEX_HOME",EnvironmentVariableTarget.User) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            s.CodexExe = Json.S(o,"codexExe");
            if (!File.Exists(s.CodexExe)) s.CodexExe = FindExe();
            if (Json.Get(o,"pinned") is bool) s.Pinned = (bool)Json.Get(o,"pinned");
            s.Left = Json.Num(Json.Get(o,"left"),-1); s.Top = Json.Num(Json.Get(o,"top"),-1);
            s.CompactLayout=(int)Json.Num(Json.Get(o,"compactLayout"));
            return s;
        }
        static string FindExe() {
            var p = Environment.GetEnvironmentVariable("CODEX_APP_RUNTIME_CLI_PATH"); if (File.Exists(p)) return p;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if (Directory.Exists(root)) { var files = Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray(); if(files.Length>0)return files[0]; }
            foreach(var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) try { p=Path.Combine(d,"codex.exe");if(File.Exists(p))return p; } catch { }
            return "";
        }
        public void Save() { Json.AtomicWrite(PathName,Json.D("codexHome",CodexHome,"codexExe",CodexExe,"pinned",false,"left",Left,"top",Top,"compactLayout",CompactLayout)); }
    }
    public sealed class DotIdentity {
        public string AccountId = "", UserId = "", Id = "", ThreadId = "", Name = "Dot";
        public DateTime? CheckedAt;
        public bool Paused;
        public bool Valid { get { return AccountId != "" && UserId != "" && Id != "" && ThreadId != ""; } }
        public static DotIdentity FromState(object state) {
            var atoms = Json.Get(state,"electron-persisted-atom-state");
            var selection = Json.Get(atoms,"primary-aeon-selection-v1"); var response = Json.Get(selection,"response"); var profile = Json.Get(response,"profile");
            var d = new DotIdentity { AccountId=Json.S(selection,"accountId"), UserId=Json.S(selection,"userId") };
            var id = Json.S(profile,"id");
            var profilesKey = "cloud-aeon-profiles-v1:" + Json.Stringify(new[]{d.AccountId,d.UserId});
            var cached = Json.Get(Json.Get(Json.Get(atoms,profilesKey),id),"profile");
            if (Json.S(cached,"id") == id && id != "") profile=cached;
            d.Id=Json.S(profile,"id"); d.ThreadId=Json.S(profile,"active_root_thread_id");
            if(d.ThreadId=="")d.ThreadId=Json.S(Json.Get(response,"selection"),"thread_id");
            if(Json.S(profile,"display_name").Trim()!="") d.Name=Json.Clean(Json.S(profile,"display_name"),40);
            d.CheckedAt=Json.Time(Json.Get(profile,"last_check_in_at"));d.Paused=Json.Get(profile,"is_paused") is bool && (bool)Json.Get(profile,"is_paused");
            return d;
        }
    }
    public sealed class DotReply {
        public string Id, Text;
        public DateTime At;
    }
    public sealed class TaskCard {
        public string Id, Title, Status, Source;
        public DateTime UpdatedAt;
        public bool Running;
    }
    public sealed class LocalSnapshot {
        public DotIdentity Dot = new DotIdentity();
        public List<DotReply> Replies = new List<DotReply>();
        public List<TaskCard> Tasks = new List<TaskCard>();
        public string Error = "", TaskError = "", FeedError = "";
    }
    public sealed class QuotaWindow {
        public string Name;
        public double Remaining;
        public DateTime? ResetAt;
        public static QuotaWindow Parse(object o, string bucket, string fallback) {
            if(o==null || Json.Get(o,"usedPercent")==null)return null;
            var used=Json.Num(Json.Get(o,"usedPercent"),double.NaN); if(double.IsNaN(used))return null;
            var mins=Json.Num(Json.Get(o,"windowDurationMins"));
            var label=mins==10080?"每周额度":mins==300?"5 小时额度":mins>=1440?(mins/1440).ToString("0.#")+" 天额度":mins>=60?(mins/60).ToString("0.#")+" 小时额度":mins>0?mins.ToString("0")+" 分钟额度":fallback;
            return new QuotaWindow { Name=(bucket=="codex"?"":bucket+" · ")+label,Remaining=Math.Max(0,Math.Min(100,100-used)),ResetAt=Json.Time(Json.Get(o,"resetsAt")) };
        }
        public static List<QuotaWindow> All(object result) {
            var windows=new List<QuotaWindow>();var buckets=Json.Obj(Json.Get(result,"rateLimitsByLimitId"));
            if(buckets.Count==0 && Json.Get(result,"rateLimits")!=null)buckets[Json.S(Json.Get(result,"rateLimits"),"limitId")==""?"codex":Json.S(Json.Get(result,"rateLimits"),"limitId")]=Json.Get(result,"rateLimits");
            foreach(var b in buckets.OrderBy(x=>x.Key=="codex"?0:1)) { var p=Parse(Json.Get(b.Value,"primary"),b.Key,"主额度");var s=Parse(Json.Get(b.Value,"secondary"),b.Key,"次额度");if(p!=null)windows.Add(p);if(s!=null)windows.Add(s); }
            return windows;
        }
    }
}
