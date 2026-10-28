using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskDots {
    public sealed class ReplyTracker {
        string scope="";
        readonly HashSet<string> seen=new HashSet<string>();
        bool seeded;
        public static string Scope(DotIdentity d){return d.Valid?d.AccountId+"|"+d.UserId+"|"+d.Id+"|"+d.ThreadId:"";}
        public List<DotReply> Observe(DotIdentity identity,IEnumerable<DotReply> replies) {
            string current=Scope(identity);
            if(current!=scope){scope=current;seen.Clear();seeded=false;}
            var rows=replies.ToList();
            if(!identity.Valid){seeded=false;return new List<DotReply>();}
            if(!seeded){foreach(var r in rows)seen.Add(r.Id);seeded=true;return new List<DotReply>();}
            return rows.OrderBy(r=>r.At).Where(r=>seen.Add(r.Id)).ToList();
        }
    }
}
