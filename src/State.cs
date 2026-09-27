using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZeroCompanyRGB
{
    internal enum Team { None, Player, Enemy, Civilian, Unknown }
    internal sealed class Member
    {
        public string Id;
        public double Health, MaxHealth, AP;
        public bool HasHealth { get { return Health>=0 && MaxHealth>0 && Health<=MaxHealth; } }
    }
    internal sealed class Snapshot
    {
        public string Session, Scene, Unit;
        public long Sequence, Stamp;
        public Team Team;
        public double Health, MaxHealth, AP;
        public double Advantage=-1, MaxAdvantage=-1;
        public int AdvantageReady=-1;
        public Member[] Members=new Member[0];
        public bool SquadComplete;
        public bool HasAdvantage { get { return Unit.Length>0 && Advantage>=0 && MaxAdvantage>0 && Advantage<=MaxAdvantage; } }
        public bool EndTurnReady
        {
            get
            {
                if(Team!=Team.Player || !Tactical || !SquadComplete || Members.Length==0)return false;
                int living=0;
                foreach(var member in Members)
                {
                    if(!member.HasHealth)return false;
                    if(member.Health==0)continue;
                    living++;
                    if(member.AP!=0)return false; // Missing/negative AP must never count as exhausted.
                }
                return living>0;
            }
        }
        public bool HasHealth { get { return Unit.Length > 0 && Health >= 0 && MaxHealth > 0 && Health <= MaxHealth; } }
        public bool HasAP { get { return Unit.Length > 0 && AP >= 0 && AP <= 100; } }
        public bool Tactical { get { return Scene.Length > 0 && Scene != "ambiguous" && (Team == Team.Player || Team == Team.Enemy || Team == Team.Civilian); } }
        public static Snapshot Parse(string text, long now)
        {
            var d = new Dictionary<string,string>(StringComparer.Ordinal);
            foreach (string line in text.Split('\n'))
            {
                string row = line.TrimEnd('\r');
                if (row.Length == 0) continue;
                int eq = row.IndexOf('=');
                if (eq < 1 || d.ContainsKey(row.Substring(0,eq))) return null;
                d.Add(row.Substring(0,eq),row.Substring(eq+1));
            }
            string[] keys = {"version","session","sequence","stamp","scene","team","unit","hp","maxhp","ap","end"};
            foreach (string key in keys) if (!d.ContainsKey(key)) return null;
            long seq, stamp, end;
            if (d["version"] != "1" || d["session"].Length == 0 ||
                !long.TryParse(d["sequence"], out seq) || seq <= 0 || !long.TryParse(d["end"],out end) || seq != end ||
                !long.TryParse(d["stamp"],out stamp) || now-stamp > 3 || stamp-now > 2) return null;
            double hp, maxhp, ap;
            if (!Number(d["hp"],out hp) || !Number(d["maxhp"],out maxhp) || !Number(d["ap"],out ap)) return null;
            Team team = Team.Unknown;
            switch (d["team"])
            {
                case "None": case "": team=Team.None;break;
                case "BitReactor.Team.Player":team=Team.Player;break;
                case "BitReactor.Team.Enemy":team=Team.Enemy;break;
                case "BitReactor.Team.Civilian":team=Team.Civilian;break;
            }
            var result = new Snapshot { Session=d["session"],Scene=d["scene"],Unit=d["unit"],Sequence=seq,Stamp=stamp,
                Team=team,Health=hp,MaxHealth=maxhp,AP=ap };
            string value;double number;int ready;
            if(d.TryGetValue("advantage",out value) && Number(value,out number))result.Advantage=number;
            if(d.TryGetValue("maxadvantage",out value) && Number(value,out number))result.MaxAdvantage=number;
            if(d.TryGetValue("advready",out value) && int.TryParse(value,out ready) && ready>=-1 && ready<=1)result.AdvantageReady=ready;
            int count;
            if(d.TryGetValue("squadcount",out value) && int.TryParse(value,out count) && count>0 && count<=32)
            {
                var members=new List<Member>();var ids=new HashSet<string>(StringComparer.Ordinal);
                for(int i=1;i<=count;i++)
                {
                    if(!d.TryGetValue("member"+i,out value))break;
                    var parts=value.Split('|');double mh,mm,ma;
                    if(parts.Length!=4 || parts[0].Length==0 || !ids.Add(parts[0]) ||
                        !Number(parts[1],out mh) || !Number(parts[2],out mm) || !Number(parts[3],out ma))break;
                    members.Add(new Member{Id=parts[0],Health=mh,MaxHealth=mm,AP=ma});
                }
                if(members.Count==count)
                {
                    result.Members=members.ToArray();
                    result.SquadComplete=d.TryGetValue("squadcomplete",out value) && value=="1";
                }
            }
            return result;
        }
        private static bool Number(string s, out double n)
        { return double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !double.IsNaN(n) && !double.IsInfinity(n); }
    }
    internal sealed class Settings
    {
        public bool Enabled=true, Turns=true, Transitions=true, Health=true, ActionPoints=true, DamageFeedback=true, Advantage=true, EndTurn=true;
        public string Exploration="BlueScan";
        public bool Release { get { return !Enabled || Exploration == "ICue"; } }
        public static Settings Parse(string text)
        {
            var s=new Settings();
            foreach(string row in text.Split('\n'))
            {
                int eq=row.IndexOf('=');if(eq<1 || row.TrimStart().StartsWith("#"))continue;
                string key=row.Substring(0,eq).Trim(),value=row.Substring(eq+1).Trim();
                if(key=="Exploration")
                { foreach(string mode in new[]{"BlueScan","Blue","Amber","ICue"})if(mode.Equals(value,StringComparison.OrdinalIgnoreCase))s.Exploration=mode;continue; }
                bool b;if(!bool.TryParse(value,out b))continue;
                switch(key) { case "Enabled":s.Enabled=b;break;case "Turns":s.Turns=b;break;
                    case "Transitions":s.Transitions=b;break;case "Health":s.Health=b;break;
                    case "ActionPoints":s.ActionPoints=b;break;case "DamageFeedback":s.DamageFeedback=b;break;
                    case "Advantage":s.Advantage=b;break;case "EndTurn":s.EndTurn=b;break; }
            }
            return s;
        }
    }
    internal sealed class Events
    {
        private Snapshot previous;
        public double Transition=-100, Hit=-100;
        public bool Healing;
        public void Reset() { previous=null;Transition=Hit=-100; }
        public void Update(Snapshot s,double now)
        {
            if(previous!=null && previous.Session==s.Session)
            {
                if(s.Sequence<=previous.Sequence)return;
                if(previous.Tactical!=s.Tactical || (previous.Tactical && s.Tactical && previous.Team!=s.Team))Transition=now;
                if(previous.Scene!=s.Scene){Hit=-100;previous=s;return;}
                bool damage=false,healing=false;
                if(s.Tactical && previous.Tactical)
                {
                    foreach(var member in s.Members)
                    {
                        if(!member.HasHealth)continue;
                        foreach(var old in previous.Members)
                        {
                            if(old.Id!=member.Id || !old.HasHealth || old.MaxHealth!=member.MaxHealth)continue;
                            damage|=member.Health<old.Health;healing|=member.Health>old.Health;
                        }
                    }
                }
                if(damage || healing){Hit=now;Healing=!damage;}
                else if(s.Members.Length==0 && previous.Members.Length==0 && previous.Unit==s.Unit && s.Unit.Length>0 &&
                    previous.Team==Team.Player && s.Team==Team.Player && previous.HasHealth && s.HasHealth &&
                    previous.MaxHealth==s.MaxHealth && previous.Health!=s.Health)
                { Hit=now;Healing=s.Health>previous.Health; }
                else if(s.Members.Length==0 && (previous.Unit!=s.Unit || previous.Team!=s.Team))Hit=-100;
            }
            else { Transition=Hit=-100; }
            previous=s;
        }
    }
}
