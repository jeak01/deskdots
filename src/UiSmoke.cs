using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace DeskDots {
    // An explicit offline UI test mode. Never reads or publishes the user's messages.
    public static class UiSmoke {
        public static int Run(string output) {
            var checks=new List<string>();string error="";int exit=0;
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window card=null,popup=null;var watch=new Stopwatch();int closeCount=0;var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"ui-check-"+Guid.NewGuid().ToString("N"));
            try {
                var widget=new Widget(new Settings{DataDir=temp,CodexHome=temp,CodexExe=""},true);card=widget.Window;
                card.Show();card.UpdateLayout();
                if(card.ActualWidth!=320||card.ActualHeight!=128)throw new Exception("compact geometry");checks.Add("320 x 128 compact geometry");
                if(card.FindName("TasksView")!=null||card.FindName("RepliesPanel")!=null)throw new Exception("unexpected permanent list");checks.Add("no permanent task or reply list");
                DesktopLayer.Sink(card);if(DesktopLayer.IsTopmost(card)||card.IsActive)throw new Exception("idle card activated or topmost");checks.Add("idle card is non-topmost and does not activate");
                widget.CheckStatusColors();checks.Add("errors are red, quota failures survive local polling, and recovery restores blue");
                widget.CheckBadgeSpacing();int spacingCount=UiSpacingAudit.Check(card);checks.Add("main controls and multi-digit badge keep content clear of edges");
                var details=widget.CheckTaskPanel(1);if(details.ActualWidth!=card.ActualWidth||details.ActualHeight>180||details.Left!=card.Left)throw new Exception("one-task panel is too large or misaligned");checks.Add("one-task panel matches card width and shrinks to content");
                spacingCount+=UiSpacingAudit.Check(details);
                details=widget.CheckTaskPanel(20);var scroll=(ScrollViewer)details.FindName("TasksScroll");details.UpdateLayout();if(details.ActualHeight>340||scroll.ExtentHeight<=scroll.ViewportHeight)throw new Exception("long task list is not capped and scrollable");checks.Add("long task list is height-capped and scrollable");
                details=widget.CheckSettingsPanel();if(details.ActualWidth!=card.ActualWidth||details.ActualHeight>240)throw new Exception("settings panel has excessive blank space");checks.Add("collapsed settings panel also fits content");spacingCount+=UiSpacingAudit.Check(details);widget.CheckExpandedSettings();spacingCount+=UiSpacingAudit.Check(details);checks.Add("collapsed and expanded settings keep content inside focus borders");details.Hide();
                Action<string> click=name=>((Button)card.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach(var button in new[]{"TasksButton","SettingsButton","DotButton"}){click(button);if(!details.IsVisible)throw new Exception(button+" did not open");click(button);if(details.IsVisible)throw new Exception(button+" did not collapse");}checks.Add("all three detail buttons open and collapse on repeated clicks");
                click("TasksButton");click("SettingsButton");if(!details.IsVisible||((ScrollViewer)details.FindName("SettingsScroll")).Visibility!=Visibility.Visible)throw new Exception("switching detail buttons failed");click("SettingsButton");checks.Add("clicking a different icon switches the detail view");
                popup=widget.CheckPopup();click("DotButton");if(popup.IsVisible)throw new Exception("Dot reply did not collapse");click("DotButton");popup=app.Windows.Cast<Window>().Single(w=>w.Title=="Dot 回复"&&w.IsVisible);checks.Add("Dot reply card also toggles closed and reopens");popup.Closed+=(o,e)=>closeCount++;watch.Start();
                if(popup.IsActive||DesktopLayer.IsTopmost(popup))throw new Exception("reply card stole focus or became topmost");checks.Add("new reply appears below the card without activation");
                spacingCount+=UiSpacingAudit.Check(popup);checks.Add(spacingCount+" rendered button instances checked in normal, hover, focus and pressed states");
                var finish=new DispatcherTimer{Interval=TimeSpan.FromSeconds(11.5)};finish.Tick+=(o,e)=>{
                    finish.Stop();try {
                        if(closeCount!=1||popup.IsVisible)throw new Exception("reply card did not close after ten seconds and fade");checks.Add("reply card fades and closes after ten seconds");
                        if(!card.IsVisible||card.Opacity!=1)throw new Exception("main card disappeared");checks.Add("quota card stays visible after the reply disappears");
                    }catch(Exception x){error=x.Message;exit=1;}finally{card.Close();app.Shutdown();}
                };finish.Start();app.Run();
            }catch(Exception e){error=e.ToString();exit=1;if(popup!=null)try{popup.Close();}catch{}if(card!=null)try{card.Close();}catch{}}
            if(Directory.Exists(temp)){foreach(var f in Directory.GetFiles(temp))File.Delete(f);Directory.Delete(temp);}
            Json.AtomicWrite(Path.GetFullPath(output),Json.D("passed",exit==0,"checks",checks.ToArray(),"elapsedSeconds",watch.Elapsed.TotalSeconds,"error",error));return exit;
        }
    }
}
