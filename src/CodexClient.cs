using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskDots {
    // A private stdio client; never resumes a thread or sends a model request.
    public sealed class CodexClient : IDisposable {
        Process process;
        readonly ConcurrentDictionary<int,TaskCompletionSource<object>> pending=new ConcurrentDictionary<int,TaskCompletionSource<object>>();
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        readonly object writeLock=new object();
        int serial;bool disposed;
        Task stderrPump,stdoutPump;
        public async Task<object> ReadQuota(Settings s) {
            await gate.WaitAsync();try {
                if(disposed)throw new ObjectDisposedException("CodexClient");
                if(process==null || process.HasExited)await Start(s);
                return await Request("account/rateLimits/read",null,25000);
            } catch { Stop();throw; } finally{gate.Release();}
        }
        async Task Start(Settings s) {
            if(!File.Exists(s.CodexExe))throw new IOException("未找到 Codex CLI，请在设置中选择 codex.exe");
            var info=new ProcessStartInfo(s.CodexExe,"app-server --stdio") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory};
            info.EnvironmentVariables["CODEX_HOME"]=s.CodexHome;
            info.EnvironmentVariables.Remove("CODEX_INTERNAL_ORIGINATOR_OVERRIDE");
            process=new Process {StartInfo=info,EnableRaisingEvents=true};
            process.Start();process.StandardInput.NewLine="\n";
            var child=process;
            stderrPump=Task.Run(async()=>{try{while(await child.StandardError.ReadLineAsync()!=null){} }catch{} }); // Drain; don't persist logs or credentials.
            stdoutPump=Task.Run(async()=>{
                try { string line;while((line=await child.StandardOutput.ReadLineAsync())!=null) {
                    object msg;try{msg=Json.Parse(line);}catch{continue;}
                    var raw=Json.Get(msg,"id");if(raw==null)continue;
                    int id=(int)Json.Num(raw,-1);TaskCompletionSource<object> tcs;
                    if(!pending.TryRemove(id,out tcs))continue;
                    if(Json.Get(msg,"error")!=null)tcs.TrySetException(new IOException("账户接口暂不可用，请检查主应用登录与网络"));else tcs.TrySetResult(Json.Get(msg,"result"));
                }} catch{} finally{if(ReferenceEquals(process,child))FailPending();}
            });
            await Request("initialize",Json.D("clientInfo",Json.D("name","deskdots","title","DeskDots","version","0.1.0"),"capabilities",Json.D("experimentalApi",false)),20000);
            Write(Json.D("method","initialized"));
        }
        async Task<object> Request(string method,object parameters,int timeout) {
            if(method!="initialize" && method!="account/rateLimits/read")throw new InvalidOperationException("DeskDots 只允许只读请求");
            int id=Interlocked.Increment(ref serial);var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=completion;
            try {var msg=Json.D("id",id,"method",method);if(parameters!=null)msg["params"]=parameters;Write(msg);
                var done=await Task.WhenAny(completion.Task,Task.Delay(timeout));if(done!=completion.Task)throw new TimeoutException("额度读取超时；点击刷新可重试");return await completion.Task;
            } finally{TaskCompletionSource<object> ignored;pending.TryRemove(id,out ignored);}
        }
        void Write(object message){lock(writeLock){process.StandardInput.WriteLine(Json.Stringify(message));process.StandardInput.Flush();}}
        void FailPending(){foreach(var p in pending){TaskCompletionSource<object> c;if(pending.TryRemove(p.Key,out c))c.TrySetException(new IOException("Codex 连接已断开；点击刷新可重连"));}}
        void Stop(){var p=process;process=null;if(p!=null){try{if(!p.HasExited)p.Kill();}catch{}p.Dispose();}FailPending();}
        public void Dispose(){disposed=true;Stop();}
    }
    public static class McpBridge {
        static object Tool(string name,string description,object schema){return Json.D("name",name,"description",description,"inputSchema",schema,"annotations",Json.D("readOnlyHint",name=="deskdots_context","destructiveHint",false,"idempotentHint",true,"openWorldHint",false));}
        public static int Run(Settings s) {
            using(var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8))using(var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true}) {
                string line;while((line=input.ReadLine())!=null){object msg=null,id=null;try {
                    msg=Json.Parse(line);id=Json.Get(msg,"id");if(id==null)continue;
                    string method=Json.S(msg,"method");object result;
                    if(method=="initialize")result=Json.D("protocolVersion","2024-11-05","capabilities",Json.D("tools",Json.D()),"serverInfo",Json.D("name","deskdots","version","0.1.0"));
                    else if(method=="ping")result=Json.D();
                    else if(method=="tools/list")result=Json.D("tools",new[]{
                        Tool("deskdots_context","读取当前 Dot 的线程 ID。只在 Dot 自己的会话使用同步工具。",Json.D("type","object","properties",Json.D(),"additionalProperties",false)),
                        Tool("deskdots_publish_dot_reply","将当前 Dot 的正式回复同步到用户本机桌面组件。不得发布普通聊天或其他 agent 的消息。重复 messageId 会替换，避免重复展示。",Json.D("type","object","properties",Json.D("dotThreadId",Json.D("type","string"),"messageId",Json.D("type","string","maxLength",256),"text",Json.D("type","string","minLength",1,"maxLength",24000)),"required",new[]{"dotThreadId","messageId","text"},"additionalProperties",false))});
                    else if(method=="tools/call") {
                        var p=Json.Get(msg,"params");object value;
                        if(Json.S(p,"name")=="deskdots_context") {var d=DotIdentity.FromState(LocalData.State(s));value=Json.D("available",d.Valid,"dotThreadId",d.ThreadId,"name",d.Name);}
                        else if(Json.S(p,"name")=="deskdots_publish_dot_reply")value=Feed.Publish(s,Json.Get(p,"arguments"));
                        else throw new ArgumentException("未知工具");
                        result=Json.D("content",new[]{Json.D("type","text","text",Json.Stringify(value))});
                    } else {output.WriteLine(Json.Stringify(Json.D("jsonrpc","2.0","id",id,"error",Json.D("code",-32601,"message","Method not found"))));continue;}
                    output.WriteLine(Json.Stringify(Json.D("jsonrpc","2.0","id",id,"result",result)));
                } catch(Exception e) {
                    if(id!=null)output.WriteLine(Json.Stringify(Json.D("jsonrpc","2.0","id",id,"error",Json.D("code",-32602,"message",e.Message))));
                }}
            }return 0;
        }
    }
}
