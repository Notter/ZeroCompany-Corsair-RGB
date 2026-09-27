using System;
using System.Runtime.InteropServices;
namespace ZeroCompanyRGB
{
    internal static class Tests
    {
        static int passed;
        static void Check(bool b,string name){if(!b)throw new Exception(name);passed++;}
        static string Data(string team="BitReactor.Team.Player",int seq=1,string unit="unit",double hp=50,string scene="scene")
        {return "version=1\nsession=test\nsequence="+seq+"\nstamp=100\nscene="+scene+"\nteam="+team+"\nunit="+unit+"\nhp="+hp+"\nmaxhp=100\nap=2\nend="+seq+"\n";}
        static void Main()
        {
            var s=Snapshot.Parse(Data(),100);
            Check(s!=null && s.Team==Team.Player && s.Tactical,"Player classification");
            Check(Snapshot.Parse(Data("BitReactor.Team.Enemy"),100).Team==Team.Enemy,"Enemy classification");
            Check(Snapshot.Parse(Data("BitReactor.Team.Civilian"),100).Team==Team.Civilian,"Civilian classification");
            Check(!Snapshot.Parse(Data("BitReactor.Team.Friendly"),100).Tactical,"Unknown teams not guessed");
            Check(!Snapshot.Parse(Data(scene:"ambiguous"),100).Tactical,"Ambiguous worlds suppressed");
            Check(Snapshot.Parse(Data(),104)==null,"Old timestamp rejected");
            Check(Snapshot.Parse(Data(),97)==null,"Future timestamp rejected");
            Check(Snapshot.Parse(Data().Replace("end=1","end=2"),100)==null,"Partial snapshot rejected");
            Check(Snapshot.Parse(Data()+"hp=30\n",100)==null,"Duplicate keys rejected");
            Check(Snapshot.Parse(Data().Replace("hp=50","hp=NaN"),100)==null,"NaN rejected");
            Check(!Snapshot.Parse(Data(hp:101),100).HasHealth,"Out of range health suppressed");
            Check(!Snapshot.Parse(Data(unit:""),100).HasAP,"No unit means no resources");
            Check(Settings.Parse("Exploration=ICue").Release,"iCUE override");
            Check(Settings.Parse("Enabled=false").Release,"Disabled override");
            Check(Settings.Parse("Exploration=invalid\nHealth=nope").Health,"Invalid settings preserve defaults");
            var e=new Events();e.Update(s,1);Check(e.Transition<0 && e.Hit<0,"No startup effects");
            e.Update(Snapshot.Parse(Data(seq:2,hp:40),100),2);Check(e.Hit==2 && !e.Healing,"Damage triggers");
            e.Update(Snapshot.Parse(Data(seq:3,hp:60),100),3);Check(e.Hit==3 && e.Healing,"Healing triggers");
            e.Update(Snapshot.Parse(Data(seq:4,hp:10,unit:"other"),100),4);Check(e.Hit<0,"Selection cannot cause damage");
            e.Update(Snapshot.Parse(Data(seq:5,hp:20,unit:"other",scene:"next"),100),5);Check(e.Hit<0,"Scene changes cannot cause damage");
            e.Update(Snapshot.Parse(Data("None",6),100),6);Check(e.Transition==6,"Leaving tactical state sweep");
            e.Reset();Check(e.Transition<0 && e.Hit<0,"Stale input clears effects");
            var layout=new[]{new Native.Position{Id=65537,Y=0},new Native.Position{Id=65542,Y=5},new Native.Position{Id=2,Y=0},new Native.Position{Id=4,Y=0},new Native.Position{Id=5,Y=0},new Native.Position{Id=71,Y=5},new Native.Position{Id=116,Y=4},new Native.Position{Id=111,Y=1}};
            var r=new Renderer(layout);var settings=new Settings();r.Render(s,settings,e,0);
            Check(r.Colors[0].G<10 && r.Colors[1].G>150,"Health fills G6 first");
            Check(r.Colors[2].G>150 && r.Colors[3].G<10,"AP uses count");
            Check(r.Colors[2].R==255 && r.Colors[2].B<15,"AP gold contrasts with player blue");
            e.Hit=0;r.Render(s,settings,e,.1);Check(r.Colors[2].G>150,"Resources survive effects");
            settings.Health=false;r.Render(s,settings,e,2);Check(r.Colors[0].G==110,"Health toggle works");
            e.Reset();var idle=Snapshot.Parse(Data("None"),100);r.Render(idle,settings,e,.5);byte upper=r.Colors[4].B,lower=r.Colors[5].B;
            r.Render(idle,settings,e,2.5);Check(r.Colors[5].B>lower && r.Colors[4].B<upper,"Scan crosses full key area");
            Check(Marshal.SizeOf(typeof(Native.Color))==8 && Marshal.SizeOf(typeof(Native.Position))==24 && Marshal.SizeOf(typeof(Native.Device))==396,"SDK ABI layouts");
            string squad="squadcount=2\nsquadcomplete=1\nmember1=a|30|30|0\nmember2=b|20|20|0\n";
            var allSpent=Snapshot.Parse(Data()+squad,100);
            Check(allSpent.EndTurnReady,"Entire known squad exhausted");
            Check(!Snapshot.Parse(Data()+squad.Replace("b|20|20|0","b|20|20|1"),100).EndTurnReady,"Other character AP blocks Space");
            Check(!Snapshot.Parse(Data()+squad.Replace("b|20|20|0","b|20|20|-1"),100).EndTurnReady,"Unknown AP blocks Space");
            Check(!Snapshot.Parse(Data()+squad.Replace("squadcomplete=1","squadcomplete=0"),100).EndTurnReady,"Incomplete roster blocks Space");
            Check(!Snapshot.Parse(Data()+squad.Replace("member2=b","member2=a"),100).EndTurnReady,"Duplicate members rejected");
            Check(!Snapshot.Parse(Data("BitReactor.Team.Enemy")+squad,100).EndTurnReady,"Never prompt during enemy turn");
            Check(Snapshot.Parse(Data()+squad.Replace("b|20|20|0","b|0|20|3"),100).EndTurnReady,"Dead unit does not block living squad");
            e.Reset();e.Update(allSpent,0);
            var enemy=Snapshot.Parse(Data("BitReactor.Team.Enemy",2)+squad.Replace("a|30|30|0","a|20|30|0"),100);
            e.Update(enemy,1);Check(e.Transition==1 && e.Hit==1 && !e.Healing,"Enemy turn transition and squad damage detected");
            var healed=Snapshot.Parse(Data("BitReactor.Team.Enemy",3)+squad.Replace("a|30|30|0","a|25|30|0"),100);
            e.Update(healed,2);Check(e.Hit==2 && e.Healing,"Healing tracked outside player turn");
            e.Reset();e.Update(allSpent,0);
            e.Update(Snapshot.Parse(Data(seq:2,unit:"other")+squad,100),1);Check(e.Hit<0,"Switching selected unit cannot flash squad damage");
            r.Render(allSpent,settings,e,3);Check(r.Colors[5].R==255 && r.Colors[5].G==5,"Space red when all AP spent");
            r.Render(s,settings,e,3);Check(r.Colors[5].R<20,"Unknown squad keeps Space neutral");
            var adv=Snapshot.Parse(Data()+"advantage=50\nmaxadvantage=100\nadvready=0\n",100);
            r.Render(adv,settings,e,3);Check(r.Colors[6].B>200 && r.Colors[7].B<20,"Advantage fills numpad bottom up");
            var full=Snapshot.Parse(Data()+"advantage=100\nmaxadvantage=100\nadvready=-1\n",100);
            r.Render(full,settings,e,3);byte steady=r.Colors[6].R;r.Render(full,settings,e,3.2);
            Check(r.Colors[6].R==steady,"Full meter alone does not claim ability ready");
            adv.AdvantageReady=1;r.Render(adv,settings,e,3);byte pulse=r.Colors[6].R;r.Render(adv,settings,e,3.2);
            Check(r.Colors[6].R!=pulse,"Confirmed ADV readiness pulses");
            settings.Advantage=false;r.Render(adv,settings,e,3);Check(r.Colors[6].R==5,"ADV toggle releases numpad to base theme");
            e.Hit=3;e.Healing=false;r.Render(enemy,settings,e,3.1);byte flash=r.Colors[4].R;
            r.Render(enemy,settings,e,3.3);Check(flash==255 && r.Colors[4].R<100,"Damage pulses visible against enemy red");
            e.Reset();e.Transition=0;r.Render(enemy,settings,e,.375);Check(r.Colors[4].R>=254 && r.Colors[4].G>=254,"Turn sweep reaches top keys brightly");
            r.Render(enemy,settings,e,1.625);Check(r.Colors[5].R>=254 && r.Colors[5].G>=254,"Turn sweep reaches bottom keys brightly");
            Console.WriteLine(passed+" checks passed.");
        }
    }
}
