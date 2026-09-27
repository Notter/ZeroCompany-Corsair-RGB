using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZeroCompanyRGB
{
    internal sealed class Keyboard : IDisposable
    {
        private volatile int state;
        private bool connected;
        private string id;
        private Native.StateCallback callback;
        private Native.Color[] previous;
        internal Renderer Renderer;
        public bool Ready { get { return state==6 && Renderer!=null; } }
        public void Connect(string folder)
        {
            Native.Load(folder);
            callback=(context,data)=> { if(data!=IntPtr.Zero)state=Marshal.ReadInt32(data); };
            Check(Native.CorsairConnect(callback,IntPtr.Zero),"Connect");connected=true;
        }
        public void Discover()
        {
            if(state!=6)return;
            int filter=1,count;
            var devices=new Native.Device[64];
            Check(Native.CorsairGetDevices(ref filter,devices.Length,devices,out count),"Keyboard discovery");
            Native.Device selected=default(Native.Device);int keyboards=0;
            for(int i=0;i<count;i++)if(devices[i].Type==1){selected=devices[i];keyboards++;}
            if(keyboards!=1)throw new InvalidOperationException("Expected one Corsair keyboard; found "+keyboards+". Lighting released.");
            var layout=new Native.Position[Math.Max(512,selected.LedCount)];
            Check(Native.CorsairGetLedPositions(selected.Id,layout.Length,layout,out count),"LED layout");
            if(count<=0)throw new InvalidOperationException("Keyboard returned an empty LED layout.");
            Array.Resize(ref layout,count);id=selected.Id;Renderer=new Renderer(layout);
            previous=new Native.Color[count];
            Program.Log("Connected: "+selected.Model+" ("+count+" LEDs).");
        }
        public void Send()
        {
            var colors=Renderer.Colors;bool changed=false;
            for(int i=0;i<colors.Length;i++)if(colors[i].R!=previous[i].R || colors[i].G!=previous[i].G || colors[i].B!=previous[i].B || colors[i].A!=previous[i].A){changed=true;break;}
            if(!changed)return;
            Check(Native.CorsairSetLedColors(id,colors.Length,colors),"LED update");
            Array.Copy(colors,previous,colors.Length);
        }
        private static void Check(int result,string action){if(result!=0)throw new InvalidOperationException(action+" returned SDK error "+result);}
        public void Dispose()
        {
            try
            {
                if(id!=null && Renderer!=null)
                {
                    var colors=Renderer.Colors;
                    for(int i=0;i<colors.Length;i++)colors[i].A=0;
                    Native.CorsairSetLedColors(id,colors.Length,colors);
                }
            }
            catch(Exception e){Program.Log("Release: "+e.Message);}
            finally
            {
                if(connected)try{Native.CorsairDisconnect();}catch(Exception e){Program.Log("Disconnect: "+e.Message);}
                connected=false;id=null;Renderer=null;state=0;
                GC.KeepAlive(callback);
            }
        }
    }
    internal static class Program
    {
        private static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
        private static string lastLog;
        internal static void Log(string message)
        {
            if(message==lastLog)return;lastLog=message;
            try { File.AppendAllText(Path.Combine(Folder,"bridge.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine); }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        private static long UnixNow(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;}
        [STAThread]
        private static void Main()
        {
            bool owner;
            using(var mutex=new Mutex(true,"Local\\ZeroCompanyCorsairRGB",out owner))
            {
                if(!owner)return;
                try
                {
                    string log=Path.Combine(Folder,"bridge.log");
                    if(File.Exists(log) && new FileInfo(log).Length>512*1024)File.Move(log,log+"."+DateTime.Now.Ticks+".old");
                    Log("Zero Company RGB 1.0.0 starting.");Run();
                }
                catch(Exception e){Log("Stopped: "+e);}
                finally{mutex.ReleaseMutex();}
            }
        }
        private static bool GameRunning()
        {
            var processes=Process.GetProcessesByName("SWZeroCompany");
            bool running=processes.Length>0;
            foreach(var process in processes)process.Dispose();
            return running;
        }
        private static void Run()
        {
            var clock=Stopwatch.StartNew();var events=new Events();var settings=new Settings();
            Snapshot snapshot=null;
            double nextRead=0,nextSettings=0,nextProcess=0,nextConnect=0,connectAt=0,lastProgress=-100;
            Keyboard keyboard=null;
            try
            {
                while(true)
                {
                    double now=clock.Elapsed.TotalSeconds;
                    if(now>=nextProcess){nextProcess=now+1;if(!GameRunning())break;}
                    if(now>=nextSettings)
                    {
                        nextSettings=now+1;
                        try{settings=Settings.Parse(File.ReadAllText(Path.Combine(Folder,"settings.ini")));}
                        catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                    if(now>=nextRead)
                    {
                        nextRead=now+.25;
                        try
                        {
                            string path=Path.Combine(Folder,"state.txt");
                            if(new FileInfo(path).Length<16384)
                            {
                                string data;
                                // Allow the Lua writer to replace its snapshot while we read;
                                // sequence/end validation rejects any incomplete frame.
                                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                                using(var reader=new StreamReader(file))data=reader.ReadToEnd();
                                Snapshot candidate=Snapshot.Parse(data,UnixNow());
                                if(candidate!=null && (snapshot==null || candidate.Session!=snapshot.Session || candidate.Sequence>snapshot.Sequence))
                                {events.Update(candidate,now);snapshot=candidate;lastProgress=now;}
                            }
                        }
                        catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                    bool active=snapshot!=null && now-lastProgress<3 && !settings.Release;
                    if(!active)
                    {
                        if(keyboard!=null){keyboard.Dispose();keyboard=null;Log("Lighting released to iCUE.");}
                        events.Reset();
                    }
                    else
                    {
                        try
                        {
                            if(keyboard==null && now>=nextConnect)
                            {keyboard=new Keyboard();connectAt=now;keyboard.Connect(Folder);}
                            if(keyboard!=null)
                            {
                                if(!keyboard.Ready)
                                {
                                    // On a lost SDK session discard the old device and frame cache.
                                    if(keyboard.Renderer!=null || now-connectAt>4)throw new InvalidOperationException("iCUE connection unavailable; retrying.");
                                    keyboard.Discover();
                                }
                                if(keyboard.Ready){keyboard.Renderer.Render(snapshot,settings,events,now);keyboard.Send();}
                            }
                        }
                        catch(Exception e)
                        {
                            Log(e.Message);if(keyboard!=null){keyboard.Dispose();keyboard=null;}nextConnect=now+3;
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            finally{if(keyboard!=null)keyboard.Dispose();Log("Game closed. Lighting released; bridge stopped.");}
        }
    }
}
