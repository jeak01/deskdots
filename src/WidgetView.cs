using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskDots {
    public sealed partial class Widget {
        Window panel;
        ReplyPopup toast;
        readonly ReplyTracker replyTracker=new ReplyTracker();
        readonly Dictionary<string,Geometry> icons=new Dictionary<string,Geometry>();
        string renderedTasks="",quotaError="",panelTrigger="";
        string localStatus="正在同步本机数据";
        bool localFailed,localReady,quotaFailed;
        SolidColorBrush Theme(string key){return (SolidColorBrush)Window.Resources[key+"Brush"];}
        void SetupVisuals() {
            accent=Theme("Accent");muted=Theme("Muted");
            Window.FontFamily=new FontFamily(new Uri(AppDomain.CurrentDomain.BaseDirectory),"./assets/fonts/#Geist, Microsoft YaHei UI");panel.FontFamily=Window.FontFamily;
            foreach(var pair in Json.Obj(Json.Parse(File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","icons.json"))))) {var geometry=Geometry.Parse(Json.Str(pair.Value));geometry.Freeze();icons[pair.Key]=geometry;}
            SetIcon("DotButton","chat-circle-dots");SetIcon("TasksButton","stack");SetIcon("SettingsButton","gear-six");SetIcon("RefreshButton","arrow-clockwise");Find<Button>("PanelClose").Content=Icon("x",12);Find<Button>("BrowseCli").Content=Icon("folder-open",12);
            Find<Button>("CopyInstructions").Content=ButtonContent("copy","复制同步说明",12);Find<Button>("OpenDataFolder").Content=ButtonContent("folder-open","本机文件",12);SetAdvancedToggle(false);
            On("PanelClose",()=>panel.Hide());On("CopyInstructions",CopyInstructions);
            On("AdvancedToggle",()=>{var fields=Find<StackPanel>("ConnectionFields");bool expanded=fields.Visibility!=Visibility.Visible;fields.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;SetAdvancedToggle(expanded);});
            On("SaveSettings",SaveConnection);
            On("BrowseCli",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Codex CLI|codex.exe|应用程序|*.exe",FileName=Find<TextBox>("CliInput").Text};if(picker.ShowDialog(panel)==true)Find<TextBox>("CliInput").Text=picker.FileName;});
            On("OpenDataFolder",()=>{try{WriteIntegration();Process.Start(new ProcessStartInfo(settings.DataDir){UseShellExecute=true});}catch(Exception e){SettingsMessage(e.Message,true);}});
            Window.SourceInitialized+=(o,e)=>DesktopLayer.Round(Window);panel.SourceInitialized+=(o,e)=>DesktopLayer.Round(panel);
            panel.KeyDown+=(o,e)=>{if(e.Key==System.Windows.Input.Key.Escape)panel.Hide();};
            panel.SizeChanged+=(o,e)=>{if(panel.IsVisible)PositionPanel();};
            AutomationProperties.SetName(Find<Button>("CopyInstructions"),"复制 Dot 同步说明");AutomationProperties.SetName(Find<Button>("OpenDataFolder"),"打开本机数据目录");
            Typography.SetNumeralAlignment(Find<TextBlock>("QuotaValue"),FontNumeralAlignment.Tabular);
        }
        void SetAdvancedToggle(bool expanded){var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.Children.Add(ButtonContent("plugs-connected","连接设置",12));var arrow=Icon("arrow-left",12);arrow.RenderTransformOrigin=new Point(.5,.5);arrow.RenderTransform=new RotateTransform(expanded?90:-90);Grid.SetColumn(arrow,1);grid.Children.Add(arrow);Find<Button>("AdvancedToggle").Content=grid;AutomationProperties.SetName(Find<Button>("AdvancedToggle"),(expanded?"收起":"展开")+"连接设置");}
        FrameworkElement Icon(string key,double size=16,Brush fill=null) {
            Geometry geometry;if(!icons.TryGetValue(key,out geometry))return new Border{Width=size,Height=size};
            var path=new System.Windows.Shapes.Path{Data=geometry,Width=size,Height=size,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center};
            if(fill==null)path.SetBinding(System.Windows.Shapes.Path.FillProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});else path.Fill=fill;return path;
        }
        void SetIcon(string control,string name){Find<Button>(control).Content=Icon(name);}
        StackPanel ButtonContent(string icon,string label,double size=14){var p=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};p.Children.Add(Icon(icon,size==12?12:16));p.Children.Add(new TextBlock{Text=label,FontSize=size,LineHeight=size==12?16:size==16?24:20,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(size==12?4:8,0,0,0),TextWrapping=TextWrapping.NoWrap});return p;}
        TextBlock Label(string text,double size=14,string color="Text"){return new TextBlock{Text=text,FontSize=size,LineHeight=size==12?16:size==14?20:size==16?24:28,Foreground=Theme(color),TextWrapping=TextWrapping.Wrap};}
        Button Action(string label,string icon,Action action,string style="SecondaryButton"){var b=new Button{Style=(Style)Window.Resources[style],Content=ButtonContent(icon,label,style=="PrimaryButton"?16:14)};b.Click+=(o,e)=>action();AutomationProperties.SetName(b,label);return b;}
        Border Surface(UIElement child,int padding=16){return new Border{Background=Theme("Surface"),CornerRadius=new CornerRadius(12),Padding=new Thickness(padding),Margin=new Thickness(0,0,0,12),Child=child};}
        void SetLocalStatus(string message,bool error){localStatus=message;localFailed=error;localReady=true;RenderStatus();}
        void RenderStatus(){var messages=new List<string>{localStatus};if(quotaFailed)messages.Add("额度同步异常："+quotaError);else if(quotaAt!=DateTime.MinValue)messages.Add("额度更新于 "+quotaAt.ToLocalTime().ToString("HH:mm:ss"));var dot=Find<Ellipse>("StatusDot");dot.Fill=localFailed||quotaFailed?Theme("Danger"):localReady?accent:muted;dot.ToolTip=string.Join("\n",messages);}
        void RenderQuota() {
            var windows=QuotaWindow.All(quotaResult);var first=windows.FirstOrDefault();
            Find<TextBlock>("QuotaValue").Text=first==null?"—":first.Remaining.ToString("0.#");Find<TextBlock>("QuotaUnit").Visibility=first==null?Visibility.Collapsed:Visibility.Visible;
            var progress=Find<ProgressBar>("QuotaProgress");progress.Value=first==null?0:first.Remaining;progress.Foreground=first!=null&&first.Remaining<=15?Theme("Warning"):accent;
            var tips=new List<string>();foreach(var w in windows)tips.Add(w.Name+" · 剩余 "+w.Remaining.ToString("0.#")+"%"+(w.ResetAt.HasValue?"\n"+w.ResetAt.Value.ToLocalTime().ToString("M月d日 HH:mm")+" 重置":""));
            if(quotaAt!=DateTime.MinValue)tips.Add("更新于 "+quotaAt.ToLocalTime().ToString("HH:mm:ss"));if(quotaError!="")tips.Add(quotaError);if(tips.Count==0)tips.Add("正在读取 Codex 额度");
            Find<FrameworkElement>("QuotaSummary").ToolTip=string.Join("\n\n",tips);progress.ToolTip=Find<FrameworkElement>("QuotaSummary").ToolTip;
            Find<TextBlock>("QuotaValue").Foreground=quotaError!=""?Theme("Secondary"):Theme("Text");
            RenderStatus();
        }
        void RenderLocal() {
            Find<Button>("DotButton").Foreground=local.Replies.Count>0?accent:Theme("Secondary");Find<Button>("DotButton").ToolTip=local.Replies.Count>0?"查看最新 Dot 回复":"连接 Dot 回复同步";
            var taskContent=new StackPanel{Orientation=Orientation.Horizontal};taskContent.Children.Add(Icon("stack"));int running=local.Tasks.Count(t=>t.Running);if(running>0){var badge=Label(running>99?"99+":running.ToString(),12,"Accent");badge.Margin=new Thickness(4,0,0,0);taskContent.Children.Add(badge);}
            var taskButton=Find<Button>("TasksButton");taskButton.Content=taskContent;taskButton.Width=double.NaN;taskButton.MinWidth=28;taskButton.Padding=running>0?new Thickness(8,6,8,6):new Thickness(6);taskButton.ToolTip=running+" 个进行中 · "+local.Tasks.Count+" 个最近任务";
            if(panel.IsVisible&&Find<ScrollViewer>("TasksScroll").Visibility==Visibility.Visible)RenderTasks();
        }
        void RenderTasks() {
            var key=Json.Stringify(Json.D("tasks",local.Tasks.Select(t=>new[]{t.Id,t.Title,t.Status,t.Source,t.UpdatedAt.ToString("o")}).ToArray(),"error",local.TaskError));if(key==renderedTasks)return;renderedTasks=key;
            var list=Find<StackPanel>("TasksPanel");list.Children.Clear();foreach(var t in local.Tasks)list.Children.Add(TaskRow(t));
            if(local.TaskError!=""){var message=Label("读取失败，稍后重试",12,"Danger");message.ToolTip=local.TaskError;list.Children.Add(message);}else if(local.Tasks.Count==0)list.Children.Add(Label("暂无近三天任务",12,"Muted"));
        }
        Border Notice(string title,string text){var p=new StackPanel();p.Children.Add(Label(title,16));var d=Label(text,12,"Secondary");d.Margin=new Thickness(0,8,0,0);p.Children.Add(d);return Surface(p);}
        UIElement TaskRow(TaskCard task) {
            var p=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Label(task.Running?"进行中":task.Status,12,task.Running?"Accent":task.Status=="失败"?"Danger":"Secondary"));var age=Label(Age(task.UpdatedAt),12,"Muted");Grid.SetColumn(age,1);header.Children.Add(age);p.Children.Add(header);
            var row=new Grid{Margin=new Thickness(0,4,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var title=Label(string.IsNullOrWhiteSpace(task.Title)?"未命名任务":task.Title,12);title.MaxHeight=32;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=task.Title;row.Children.Add(title);var arrow=Icon("arrow-up-right",12,accent);arrow.Margin=new Thickness(8,0,0,0);Grid.SetColumn(arrow,1);row.Children.Add(arrow);p.Children.Add(row);
            var open=new Button{Style=(Style)panel.Resources["BaseButton"],Content=p,Background=Theme("Surface"),Padding=new Thickness(8),HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,4),ToolTip=task.Title};open.Click+=(o,e)=>OpenThread(task.Id);AutomationProperties.SetName(open,"打开任务："+task.Title);return open;
        }
        void ShowPanel(bool settingsPage,bool activate=true) {
            DismissReplies();panelTrigger=settingsPage?"settings":"tasks";
            Find<ScrollViewer>("SettingsScroll").Visibility=settingsPage?Visibility.Visible:Visibility.Collapsed;Find<ScrollViewer>("TasksScroll").Visibility=settingsPage?Visibility.Collapsed:Visibility.Visible;Find<TextBlock>("PanelTitle").Text=settingsPage?"设置":"任务";Find<TextBlock>("TaskScope").Visibility=settingsPage?Visibility.Collapsed:Visibility.Visible;
            var area=SystemParameters.WorkArea;double room=Math.Max(area.Bottom-Window.Top-Window.Height-24,Window.Top-area.Top-24);room=Math.Max(120,room);panel.Width=Window.Width;panel.MaxHeight=room;
            Find<ScrollViewer>("TasksScroll").MaxHeight=Math.Max(64,Math.Min(256,room-46));Find<ScrollViewer>("SettingsScroll").MaxHeight=Math.Max(64,Math.Min(440,room-46));
            panel.ShowActivated=activate;panel.Show();panel.UpdateLayout();PositionPanel();if(activate)panel.Activate();else DesktopLayer.Sink(panel);
        }
        void PositionPanel(){var area=SystemParameters.WorkArea;panel.Left=Window.Left;double below=Window.Top+Window.Height+8;panel.Top=below+panel.ActualHeight<=area.Bottom-16?below:Math.Max(area.Top+16,Window.Top-panel.ActualHeight-8);}
        void ToggleDetails(string trigger){bool open=(panel.IsVisible&&panelTrigger==trigger)||(trigger=="dot"&&toast!=null&&toast.Window.IsVisible);panel.Hide();DismissReplies();if(open){panelTrigger="";return;}if(trigger=="dot"&&local.Replies.Count>0)ShowReply(local.Replies[0]);else if(trigger=="tasks")ShowTasks();else{ShowSettings();panelTrigger=trigger;}}
        void ShowTasks(){RenderTasks();ShowPanel(false);}
        void ShowSettings(){Find<TextBox>("HomeInput").Text=settings.CodexHome;Find<TextBox>("CliInput").Text=settings.CodexExe;Find<TextBlock>("DataLocation").Text=settings.DataDir;Find<TextBlock>("CopyFeedback").Visibility=Visibility.Collapsed;Find<TextBlock>("SettingsError").Visibility=Visibility.Collapsed;Find<Button>("CopyInstructions").IsEnabled=local.Dot.Valid;WriteIntegration();ShowPanel(true);}
        void CopyInstructions(){try{Clipboard.SetText(IntegrationPrompt());var note=Find<TextBlock>("CopyFeedback");note.Text="已复制，请粘贴到 Dot 会话并发送。";note.Visibility=Visibility.Visible;WriteIntegration();}catch{SettingsMessage("剪贴板暂不可用，请重试。",true);}}
        void SettingsMessage(string text,bool error){var m=Find<TextBlock>("SettingsError");m.Text=text;m.Foreground=Theme(error?"Danger":"Accent");m.Visibility=Visibility.Visible;}
        void SaveConnection(){if(quotaBusy||localBusy){SettingsMessage("正在同步，请稍后保存。",false);return;}string home=Find<TextBox>("HomeInput").Text.Trim(),cli=Find<TextBox>("CliInput").Text.Trim();if(!Directory.Exists(home)||!File.Exists(cli)){SettingsMessage("请检查数据目录和 codex.exe 路径。",true);return;}settings.CodexHome=home;settings.CodexExe=cli;client.Dispose();client=new CodexClient();quotaResult=null;quotaAt=DateTime.MinValue;fingerprint="";renderedTasks="";Save();WriteIntegration();RenderQuota();SettingsMessage("已保存。",false);nextQuota=DateTime.MinValue;Tick();}
        void SinkIdle(){if(closed)return;if(Window.IsActive||panel.IsActive||(toast!=null&&toast.Window.IsActive))return;DesktopLayer.Sink(Window);if(panel.IsVisible)DesktopLayer.Sink(panel);if(toast!=null)DesktopLayer.Sink(toast.Window);}
        void DismissReplies(){var old=toast;toast=null;if(old!=null)old.Close();}
        void PositionPopups(){if(panel!=null&&panel.IsVisible)PositionPanel();if(toast==null)return;var area=SystemParameters.WorkArea;var h=toast.Window.ActualHeight>0?toast.Window.ActualHeight:180;toast.Window.Left=Window.Left;toast.Window.Top=Math.Max(area.Top+16,Math.Min(area.Bottom-h-16,Window.Top+Window.Height+8));}
        void ShowReply(DotReply reply) {
            if(!Window.IsVisible)return;
            if(toast!=null&&toast.Id==reply.Id){toast.Reset();return;}
            DismissReplies();var body=new StackPanel();var head=new Grid{Margin=new Thickness(0,0,0,8)};head.ColumnDefinitions.Add(new ColumnDefinition());head.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});head.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var name=Label(local.Dot.Name,12,"Secondary");name.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(name);
            var copy=new Button{Style=(Style)Window.Resources["IconButton"],Width=20,Height=20,Padding=new Thickness(4),Content=Icon("copy",12),ToolTip="复制回复"};copy.Click+=(o,e)=>{try{Clipboard.SetText(reply.Text);}catch{}};Grid.SetColumn(copy,1);head.Children.Add(copy);
            var close=new Button{Style=(Style)Window.Resources["IconButton"],Width=20,Height=20,Padding=new Thickness(4),Content=Icon("x",12),ToolTip="关闭消息"};close.Click+=(o,e)=>DismissReplies();Grid.SetColumn(close,2);head.Children.Add(close);body.Children.Add(head);
            var text=new TextBox{Text=reply.Text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brushes.Transparent,Foreground=Theme("Text"),BorderThickness=new Thickness(0),FontSize=12,Padding=new Thickness(0),SelectionBrush=accent,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};TextBlock.SetLineHeight(text,16);
            body.Children.Add(new ScrollViewer{Content=text,MaxHeight=128,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var bubble=new Window{Title="Dot 回复",Width=Window.Width,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=false,AllowsTransparency=true,Background=Brushes.Transparent,Foreground=Theme("Text"),FontFamily=Window.FontFamily,FontSize=12,Resources=Window.Resources,Content=new Border{Background=Theme("Surface"),BorderBrush=Theme("Stroke"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(12,8,12,12),Child=body}};
            bubble.ToolTip="10 秒后自动淡出";AutomationProperties.SetName(copy,"复制 Dot 回复");AutomationProperties.SetName(close,"关闭 Dot 回复");
            var current=new ReplyPopup(reply.Id,bubble);toast=current;bubble.Closed+=(o,e)=>{if(ReferenceEquals(toast,current))toast=null;};bubble.Deactivated+=(o,e)=>Window.Dispatcher.BeginInvoke(new Action(SinkIdle));bubble.SizeChanged+=(o,e)=>PositionPopups();
            bubble.Left=Window.Left;bubble.Top=Window.Top+Window.Height+8;bubble.Show();PositionPopups();current.Reset();SinkIdle();
        }
        internal Window CheckPopup(){local.Dot.Name="Dot · 离线检查";local.Replies=new List<DotReply>{new DotReply{Id="ui-check",Text="Dot 通知计时检查，十秒后自动淡出。",At=DateTime.UtcNow}};ShowReply(local.Replies[0]);return toast.Window;}
        internal Window CheckTaskPanel(int count){local.Tasks=Enumerable.Range(1,count).Select(i=>new TaskCard{Id="check-"+i,Title="界面检查任务 "+i,Status="已完成",Source="Codex",UpdatedAt=DateTime.UtcNow}).ToList();renderedTasks="";RenderTasks();ShowPanel(false,false);return panel;}
        internal Window CheckSettingsPanel(){ShowPanel(true,false);return panel;}
        internal void CheckBadgeSpacing(){local.Tasks=Enumerable.Range(1,120).Select(i=>new TaskCard{Id="badge-"+i,Title="界面检查",Status="进行中",Running=true,Source="Codex",UpdatedAt=DateTime.UtcNow}).ToList();quotaResult=Json.D("rateLimits",Json.D("primary",Json.D("usedPercent",0.1,"windowDurationMins",10080)));RenderQuota();RenderLocal();Window.UpdateLayout();}
        internal void CheckExpandedSettings(){Find<StackPanel>("ConnectionFields").Visibility=Visibility.Visible;SetAdvancedToggle(true);panel.UpdateLayout();}
        internal void CheckStatusColors(){SetLocalStatus("读取失败",true);if(!ReferenceEquals(Find<Ellipse>("StatusDot").Fill,Theme("Danger")))throw new Exception("local error must be red");quotaFailed=true;quotaError="连接失败";SetLocalStatus("本机读取成功",false);if(!ReferenceEquals(Find<Ellipse>("StatusDot").Fill,Theme("Danger")))throw new Exception("local success must not hide a quota error");quotaFailed=false;quotaError="";RenderStatus();if(!ReferenceEquals(Find<Ellipse>("StatusDot").Fill,accent))throw new Exception("recovery must restore blue");}
        public void SaveSettingsPreview(string file){ShowSettings();Find<StackPanel>("ConnectionFields").Visibility=Visibility.Visible;SetAdvancedToggle(true);panel.UpdateLayout();Find<Button>("AdvancedToggle").Focus();panel.UpdateLayout();SaveWindowPreview(panel,file);SaveWindowPreview(Find<Button>("AdvancedToggle"),file+".button.png");panel.Hide();}
        public void SaveCardPreview(string file){SaveWindowPreview(Window,file);}
        public void SaveTaskPreview(string file){RenderTasks();ShowPanel(false,false);SaveWindowPreview(panel,file);panel.Hide();}
        static void SaveWindowPreview(FrameworkElement target,string file){target.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);if(target is Window)bitmap.Render(target);else{var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(target),null,new Rect(0,0,target.ActualWidth,target.ActualHeight));bitmap.Render(visual);}var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(file))encoder.Save(stream);}
    }
}
