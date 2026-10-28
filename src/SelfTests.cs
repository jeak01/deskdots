using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskDots {
    public static class SelfTests {
        public static int Run(string output) {
            var results=new List<string>();
            try {
                var d=new DotIdentity{AccountId="a",UserId="u",Id="dot",ThreadId="thread"};
                Func<string,string,string,object> reply=(id,role,source)=>Json.D("id",id,"role",role,"source",source,"dotThreadId","thread","createdAt","2026-10-28T01:00:00Z","text","测试回复");
                var feed=Json.D("accountId","a","userId","u","dotId","dot","replies",new[]{reply("dot-reply","assistant","dot"),reply("user","user","dot"),reply("ordinary","assistant","codex"),Json.D("id","other-thread","role","assistant","source","dot","dotThreadId","other","createdAt","2026-10-28T01:00:00Z","text","不应出现")});
                Check(Feed.Parse(Roundtrip(feed),d).Count==1,"only Dot assistant replies pass",results);
                feed["accountId"]="other";Check(Feed.Parse(Roundtrip(feed),d).Count==0,"different accounts are isolated",results);feed["accountId"]="a";
                feed["userId"]="other";Check(Feed.Parse(Roundtrip(feed),d).Count==0,"different users are isolated",results);feed["userId"]="u";
                feed["dotId"]="other";Check(Feed.Parse(Roundtrip(feed),d).Count==0,"different Dots are isolated",results);feed["dotId"]="dot";
                feed["replies"]=new[]{reply("same","assistant","dot"),reply("same","assistant","dot")};Check(Feed.Parse(Roundtrip(feed),d).Count==1,"message IDs are deduplicated",results);
                Check(QuotaWindow.All(Roundtrip(Json.D("rateLimits",Json.D("primary",null,"secondary",null)))).Count==0,"unknown quota stays unknown",results);
                var quota=Json.D("rateLimitsByLimitId",Json.D("codex",Json.D("primary",Json.D("usedPercent",29,"windowDurationMins",10080,"resetsAt",1793599642),"secondary",null),"other",Json.D("primary",Json.D("usedPercent",0,"windowDurationMins",300),"secondary",Json.D("usedPercent",110,"windowDurationMins",1440))));
                var windows=QuotaWindow.All(Roundtrip(quota));Check(windows.Count==3 && windows[0].Remaining==71 && windows[1].Remaining==100 && windows[2].Remaining==0,"multiple quota buckets and clamping",results);
                Check(windows[0].Name=="每周额度" && windows[0].ResetAt.HasValue,"window labels and reset time",results);
                Check(QuotaWindow.Parse(Json.D("usedPercent","invalid"),"codex","")==null,"malformed quota is not zero",results);
                Check(Json.Time(1793599642)==Json.Time(1793599642000d),"seconds and milliseconds timestamps",results);
                Check(Json.Time("not-a-date")==null,"invalid timestamps rejected",results);
                var state=Json.D("electron-persisted-atom-state",Json.D("primary-aeon-selection-v1",Json.D("accountId","a","userId","u","response",Json.D("selection",Json.D("thread_id","t"),"profile",Json.D("id","dot","display_name","","active_root_thread_id","t")))));
                var dot=DotIdentity.FromState(Roundtrip(state));Check(dot.Valid && dot.Name=="Dot" && dot.ThreadId=="t","Dot identity from current selection",results);
                var tracker=new ReplyTracker();var oldReply=new DotReply{Id="old",Text="old",At=DateTime.UtcNow.AddDays(-1)};var freshReply=new DotReply{Id="new",Text="new",At=DateTime.UtcNow};
                Check(tracker.Observe(dot,new[]{oldReply}).Count==0,"startup history does not trigger popups",results);
                Check(tracker.Observe(dot,new[]{freshReply,oldReply}).Single().Id=="new","a new Dot reply triggers once",results);
                Check(tracker.Observe(dot,new[]{freshReply,oldReply}).Count==0,"polling does not replay a reply",results);
                var switched=new DotIdentity{AccountId="another",UserId="u",Id="dot",ThreadId="t"};
                Check(tracker.Observe(switched,new[]{freshReply}).Count==0,"account changes reset the popup baseline",results);
                Check(tracker.Observe(new DotIdentity(),new[]{freshReply}).Count==0,"unavailable Dot cannot create popups",results);
                var tmp=Path.Combine(output==null?AppDomain.CurrentDomain.BaseDirectory:Path.GetDirectoryName(Path.GetFullPath(output)),"deskdots-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(tmp);
                try {var s=new Settings{CodexHome=tmp,DataDir=tmp};Json.AtomicWrite(Path.Combine(tmp,".codex-global-state.json"),state);
                    Feed.Publish(s,Json.D("dotThreadId","t","messageId","m","text","first"));Feed.Publish(s,Json.D("dotThreadId","t","messageId","m","text","updated"));
                    var replies=Feed.Read(s,dot);Check(replies.Count==1 && replies[0].Text=="updated","atomic publication and idempotent retry",results);
                    bool denied=false;try{Feed.Publish(s,Json.D("dotThreadId","ordinary-chat","messageId","n","text","rejected"));}catch(InvalidOperationException){denied=true;}
                    Check(denied && Feed.Read(s,dot).Count==1,"ordinary thread publication rejected",results);
                } finally{foreach(var f in Directory.GetFiles(tmp))File.Delete(f);Directory.Delete(tmp);}
                if(output!=null)Json.AtomicWrite(Path.GetFullPath(output),Json.D("passed",true,"count",results.Count,"checks",results.ToArray()));return 0;
            } catch(Exception e){if(output!=null)Json.AtomicWrite(Path.GetFullPath(output),Json.D("passed",false,"checks",results.ToArray(),"error",e.ToString()));return 1;}
        }
        static object Roundtrip(object o){return Json.Parse(Json.Stringify(o));}
        static void Check(bool condition,string name,List<string> results){if(!condition)throw new Exception(name);results.Add(name);}
    }
}
