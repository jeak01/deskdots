using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace DeskDots {
    public static class Program {
        public static EventWaitHandle ShowSignal;
        [STAThread] public static int Main(string[] args) {
            string data=Option(args,"--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskDots");
            try {
                var settings=Settings.Load(Path.GetFullPath(data));
                if(args.Contains("--mcp"))return McpBridge.Run(settings);
                if(args.Contains("--self-test"))return SelfTests.Run(Option(args,"--result"));
                var uiSmoke=Option(args,"--ui-smoke");if(uiSmoke!=null)return UiSmoke.Run(uiSmoke);
                var publish=Option(args,"--publish-file");if(publish!=null){Feed.Publish(settings,Json.Parse(File.ReadAllText(publish)));return 0;}
                var diagnose=Option(args,"--diagnose");if(diagnose!=null){Diagnose(settings,diagnose,!args.Contains("--local-only")).GetAwaiter().GetResult();return 0;}
                using(var mutex=new Mutex(false,"Local\\DeskDotsWidget-"+Hash(settings.DataDir))) using(ShowSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DeskDotsShow-"+Hash(settings.DataDir))) {
                    bool held=false;try {try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
                        if(!held){ShowSignal.Set();return 0;}
                        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.DispatcherUnhandledException+=(o,e)=>{MessageBox.Show(e.Exception.Message,"DeskDots");e.Handled=true;};
                        var widget=new Widget(settings);var capture=Option(args,"--capture-card");var detailsCapture=Option(args,"--capture-details");var settingsCapture=Option(args,"--capture-settings");if(capture!=null||detailsCapture!=null||settingsCapture!=null)widget.Window.Loaded+=async(o,e)=>{await Task.Delay(1500);try{if(capture!=null)widget.SaveCardPreview(capture);if(detailsCapture!=null)widget.SaveTaskPreview(detailsCapture);if(settingsCapture!=null)widget.SaveSettingsPreview(settingsCapture);}catch{}};app.MainWindow=widget.Window;return app.Run(widget.Window);
                    } finally{if(held)mutex.ReleaseMutex();}
                }
            } catch(Exception e) {
                try { Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"last-error.txt"),e.ToString()); }catch{}
                if(!args.Any(a=>a.StartsWith("--")))MessageBox.Show(e.Message,"DeskDots 启动失败");return 1;
            }
        }
        static string Hash(string text){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Replace("-","").Substring(0,20);}
        static string Option(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        static async Task Diagnose(Settings s,string output,bool network) {
            var snap=LocalData.Read(s);object quota=null;string err="";
            if(network)using(var client=new CodexClient())try{quota=await client.ReadQuota(s);}catch(Exception e){err=e.Message;}
            Json.AtomicWrite(Path.GetFullPath(output),Json.D("dotAvailable",snap.Dot.Valid,"dotName",snap.Dot.Name,"replyCount",snap.Replies.Count,"taskCount",snap.Tasks.Count,"runningCount",snap.Tasks.Count(t=>t.Running),"localError",snap.Error,"taskError",snap.TaskError,"quotaWindows",QuotaWindow.All(quota).Select(w=>Json.D("name",w.Name,"remaining",w.Remaining)).ToArray(),"quotaError",err));
        }
    }
    public sealed partial class Widget {
        public readonly Window Window;
        Settings settings;
        CodexClient client=new CodexClient();
        LocalSnapshot local=new LocalSnapshot();
        Forms.NotifyIcon tray;
        DispatcherTimer timer,quotaTimer;
        DateTime nextQuota=DateTime.MinValue,quotaAt=DateTime.MinValue;
        bool localBusy,quotaBusy,closed;
        object quotaResult;
        string fingerprint="";
        System.Threading.RegisteredWaitHandle showWait;
        SolidColorBrush accent,muted;
        public Widget(Settings s,bool offline=false) {
            settings=s;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskDots.App.xaml"))Window=(Window)XamlReader.Load(stream);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskDots.Panel.xaml"))panel=(Window)XamlReader.Load(stream);
            SetupVisuals();Window.Topmost=false;settings.Pinned=false;
            var screen=SystemParameters.WorkArea;
            bool migrate=s.CompactLayout<1;
            Window.Left=Math.Max(screen.Left+16,Math.Min(screen.Right-Window.Width-16,migrate||s.Left<screen.Left?screen.Right-Window.Width-16:s.Left));
            Window.Top=Math.Max(screen.Top+16,Math.Min(screen.Bottom-Window.Height-16,migrate||s.Top<screen.Top?screen.Top+16:s.Top));
            settings.CompactLayout=1;
            Find<FrameworkElement>("CardSurface").MouseLeftButtonDown+=(o,e)=>{
                DependencyObject node=e.OriginalSource as DependencyObject;
                while(node!=null){if(node is System.Windows.Controls.Primitives.ButtonBase)return;node=VisualTreeHelper.GetParent(node);}
                Window.Activate();try{Window.DragMove();}catch{}PositionPopups();
            };
            On("DotButton",()=>ToggleDetails("dot"));
            On("TasksButton",()=>ToggleDetails("tasks"));On("SettingsButton",()=>ToggleDetails("settings"));
            On("RefreshButton",()=>{nextQuota=DateTime.MinValue;Tick();});
            Window.KeyDown+=(o,e)=>{if(e.Key==Key.Escape)Window.Hide();if(e.Key==Key.F5){nextQuota=DateTime.MinValue;Tick();}};
            Window.Deactivated+=(o,e)=>Window.Dispatcher.BeginInvoke(new Action(SinkIdle));
            panel.Deactivated+=(o,e)=>Window.Dispatcher.BeginInvoke(new Action(SinkIdle));
            panel.Closing+=(o,e)=>{if(!closed){e.Cancel=true;panel.Hide();SinkIdle();}};
            Window.IsVisibleChanged+=(o,e)=>{if(!Window.IsVisible){DismissReplies();panel.Hide();}};
            Window.Closed+=(o,e)=>{closed=true;timer.Stop();quotaTimer.Stop();DismissReplies();panel.Close();if(showWait!=null)showWait.Unregister(null);Save();client.Dispose();tray.Visible=false;tray.Dispose();};
            Window.LocationChanged+=(o,e)=>{settings.Left=Window.Left;settings.Top=Window.Top;PositionPopups();};
            MakeTray();if(!offline)LoadQuota();
            if(Program.ShowSignal!=null)showWait=ThreadPool.RegisterWaitForSingleObject(Program.ShowSignal,(o,t)=>Window.Dispatcher.BeginInvoke(new Action(Show)),null,Timeout.Infinite,false);
            timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};timer.Tick+=(o,e)=>{RefreshLocal();SinkIdle();};
            quotaTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(60)};quotaTimer.Tick+=(o,e)=>RefreshQuota();if(!offline){timer.Start();quotaTimer.Start();}
            Window.Loaded+=(o,e)=>{if(!offline){Save();WriteIntegration();Tick();}Window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(SinkIdle));};
        }
        static SolidColorBrush Brush(string color){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));b.Freeze();return b;}
        T Find<T>(string name) where T:class{return (Window.FindName(name) ?? panel.FindName(name)) as T;}
        void On(string name,Action action){Find<Button>(name).Click+=(o,e)=>action();}
        void Save(){try{settings.Save();}catch{}}
        void MakeTray() {
            var icon=MakeIcon();tray=new Forms.NotifyIcon{Icon=icon,Text="DeskDots · Dot 消息与 Codex 额度",Visible=true};
            tray.DoubleClick+=(o,e)=>Show();var menu=new Forms.ContextMenuStrip();menu.Items.Add("显示组件",null,(o,e)=>Show());menu.Items.Add("刷新",null,(o,e)=>{nextQuota=DateTime.MinValue;Tick();});menu.Items.Add("设置 / Dot 同步",null,(o,e)=>ShowSettings());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(o,e)=>Window.Close());tray.ContextMenuStrip=menu;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        System.Drawing.Icon MakeIcon(){using(var bmp=new System.Drawing.Bitmap(32,32))using(var g=System.Drawing.Graphics.FromImage(bmp)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(System.Drawing.Color.Transparent);using(var b=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(accent.Color.R,accent.Color.G,accent.Color.B)))g.FillEllipse(b,2,2,28,28);using(var p=new System.Drawing.Pen(System.Drawing.Color.FromArgb(24,24,24),3)){g.DrawLine(p,11,12,11,16);g.DrawLine(p,21,12,21,16);g.DrawArc(p,11,15,10,8,10,160);}var h=bmp.GetHicon();var clone=(System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();DestroyIcon(h);return clone;}}
        void Show(){Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();}
        void Tick(){if(closed)return;RefreshLocal();if(DateTime.UtcNow>=nextQuota)RefreshQuota();SinkIdle();}
        async void RefreshLocal() {
            if(localBusy || closed)return;localBusy=true;
            try {var snap=await Task.Run(()=>LocalData.Read(settings));if(closed)return;
                var key=Json.Stringify(Json.D("dot",snap.Dot.ThreadId,"check",snap.Dot.CheckedAt.HasValue?snap.Dot.CheckedAt.Value.ToString("o"):"","replies",snap.Replies.Select(r=>new[]{r.Id,r.Text,r.At.ToString("o")}).ToArray(),"tasks",snap.Tasks.Select(t=>new[]{t.Id,t.Title,t.Status,t.UpdatedAt.ToString("o")}).ToArray(),"errors",new[]{snap.Error,snap.TaskError,snap.FeedError}));
                var changedDot=ReplyTracker.Scope(local.Dot)!=ReplyTracker.Scope(snap.Dot);if(changedDot)DismissReplies();var arrived=replyTracker.Observe(snap.Dot,snap.Replies);local=snap;if(key!=fingerprint){fingerprint=key;RenderLocal();}if(changedDot)WriteIntegration();if(Window.IsVisible)foreach(var reply in arrived)ShowReply(reply);
                var errors=new[]{snap.Error,snap.TaskError,snap.FeedError}.Where(v=>!string.IsNullOrWhiteSpace(v)).ToArray();SetLocalStatus(errors.Length>0?string.Join("\n",errors):"本机同步于 "+DateTime.Now.ToString("HH:mm:ss"),errors.Length>0);
            } catch(Exception e){if(!closed)SetLocalStatus("本机同步失败："+e.Message,true);}
            finally{localBusy=false;}
        }
        async void RefreshQuota() {
            if(quotaBusy || closed)return;quotaBusy=true;nextQuota=DateTime.UtcNow.AddMinutes(1);
            Find<Button>("RefreshButton").IsEnabled=false;
            try {var result=await client.ReadQuota(settings);if(closed)return;quotaResult=result;quotaAt=DateTime.UtcNow;quotaError="";quotaFailed=false;RenderQuota();
                Json.AtomicWrite(Path.Combine(settings.DataDir,"quota-cache.json"),Json.D("fetchedAt",quotaAt.ToString("o"),"result",Json.D("rateLimits",Json.Get(result,"rateLimits"),"rateLimitsByLimitId",Json.Get(result,"rateLimitsByLimitId"),"accountId",Json.Get(result,"accountId"))));
            } catch(Exception e){if(!closed){quotaError=e.Message;quotaFailed=true;RenderQuota();}}
            finally{quotaBusy=false;if(!closed)Find<Button>("RefreshButton").IsEnabled=true;}
        }
        void LoadQuota(){try{var file=Path.Combine(settings.DataDir,"quota-cache.json");if(File.Exists(file)){var v=Json.Parse(File.ReadAllText(file));var identity=DotIdentity.FromState(LocalData.State(settings));var result=Json.Get(v,"result");if(identity.Valid && Json.S(result,"accountId")!=identity.AccountId)return;quotaResult=result;quotaAt=Json.Time(Json.Get(v,"fetchedAt"))??DateTime.MinValue;quotaError="缓存，等待刷新";RenderQuota();}}catch{}}
        static string Age(DateTime at){var t=DateTime.UtcNow-at;return t.TotalMinutes<1?"刚刚":t.TotalHours<1?(int)t.TotalMinutes+" 分钟前":t.TotalDays<1?(int)t.TotalHours+" 小时前":at.ToLocalTime().ToString("M/d HH:mm");}
        void OpenThread(string id){Guid parsed;if(!Guid.TryParse(id,out parsed))return;try{Process.Start(new ProcessStartInfo("codex://threads/"+Uri.EscapeDataString(id)){UseShellExecute=true});}catch(Exception e){SetLocalStatus("打开失败："+e.Message,true);}}
        void WriteIntegration(){try{File.WriteAllText(Path.Combine(settings.DataDir,"给Dot的同步说明.txt"),IntegrationPrompt(),new UTF8Encoding(false));File.WriteAllText(Path.Combine(settings.DataDir,"mcp-config.toml"),"[mcp_servers.deskdots]\ncommand = '"+Assembly.GetExecutingAssembly().Location.Replace("'","''")+"'\nargs = ['--mcp', '--data-dir', '"+settings.DataDir.Replace("'","''")+"']\n",new UTF8Encoding(false));}catch{}}
        string IntegrationPrompt() {
            string exe=Assembly.GetExecutingAssembly().Location,outbox=Path.Combine(settings.DataDir,"dot-outbox.json");
            return "请为你（Dot）自己启用 DeskDots 本机回复同步。仅同步你在当前 Dot 会话中的正式回复，不能同步普通 Codex 聊天、用户消息、工具结果或其他 agent 的输出。\r\n\r\n当前 Dot 线程 ID："+local.Dot.ThreadId+"\r\n\r\n从现在起，每次正式回复后，通过已连接的 Windows 电脑把回复写成 UTF-8 JSON 文件：\r\n"+outbox+"\r\n\r\nJSON 字段：dotThreadId（上面的线程 ID）、messageId（为这条回复生成稳定唯一 ID，重试保持同一 ID）、text（你的完整正式回复正文）。然后通过 shell 执行以下 PowerShell 调用：\r\n& '"+exe.Replace("'","''")+"' --data-dir '"+settings.DataDir.Replace("'","''")+"' --publish-file '"+outbox.Replace("'","''")+"'\r\n\r\n这是用户授权的本机同步，不发送任何外部消息。写入失败时请告知，不能声称已经同步。若无法访问本机文件或执行命令，请说明限制。若已经连接 deskdots MCP，可改用 deskdots_context 和 deskdots_publish_dot_reply；不要重复发布。";
        }
    }
}
