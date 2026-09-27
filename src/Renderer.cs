using System;

namespace ZeroCompanyRGB
{
    internal sealed class Renderer
    {
        private readonly Native.Position[] positions;
        private readonly double minY, height;
        private readonly double[] normalizedY;
        private readonly int[] advantageSlots;
        internal readonly Native.Color[] Colors;
        private static readonly uint[] AdvantageKeys={116,117,118,113,114,115,109,110,111};
        public Renderer(Native.Position[] layout)
        {
            positions=layout;Colors=new Native.Color[layout.Length];
            double lo=double.MaxValue,hi=double.MinValue;
            for(int i=0;i<layout.Length;i++)
            {
                Colors[i].Id=layout[i].Id;
                if(layout[i].Id<65536) { lo=Math.Min(lo,layout[i].Y);hi=Math.Max(hi,layout[i].Y); }
            }
            minY=lo==double.MaxValue?0:lo;height=Math.Max(1,hi-minY);
            normalizedY=new double[layout.Length];advantageSlots=new int[layout.Length];
            for(int i=0;i<layout.Length;i++)
            {normalizedY[i]=(layout[i].Y-minY)/height;advantageSlots[i]=Array.IndexOf(AdvantageKeys,layout[i].Id);}
        }
        public void Render(Snapshot s,Settings settings,Events events,double time)
        {
            double r=8,g=42,b=95;
            if(settings.Exploration=="Amber") {r=115;g=65;b=5;}
            if(settings.Turns && s.Tactical)
            {
                if(s.Team==Team.Player){r=5;g=110;b=155;}
                if(s.Team==Team.Enemy){r=150;g=12;b=5;}
                if(s.Team==Team.Civilian){r=145;g=85;b=6;}
            }
            double transitionAge=time-events.Transition,hitAge=time-events.Hit;
            double pulse=.65+.35*Math.Sin(time*5);
            for(int i=0;i<positions.Length;i++)
            {
                uint id=positions[i].Id;
                double y=normalizedY[i];
                double rr=r,gg=g,bb=b;
                if(settings.Exploration=="BlueScan" && !s.Tactical)
                {
                    double scan=time%3/3*1.5-.25;
                    double glow=Math.Max(0,1-Math.Abs(y-scan)/.23);
                    rr+=glow*45;gg+=glow*145;bb+=glow*140;
                }
                if(settings.Transitions && transitionAge>=0 && transitionAge<2)
                {
                    double center=-.3+transitionAge/2*1.6;
                    double glow=Math.Max(0,1-Math.Abs(y-center)/.3);
                    rr=rr*(1-glow)+255*glow;gg=gg*(1-glow)+255*glow;bb=bb*(1-glow)+255*glow;
                }
                if(settings.DamageFeedback && hitAge>=0 && hitAge<1.25)
                {
                    // Two strong pulses with a dark interval stay visible even on enemy red.
                    double f=(hitAge<.22 || (hitAge>=.4 && hitAge<.62))?1:Math.Max(0,(1.25-hitAge)/1.25)*.25;
                    rr=rr*.18*(1-f)+(events.Healing?15:255)*f;
                    gg=gg*.18*(1-f)+(events.Healing?255:38)*f;
                    bb=bb*.18*(1-f)+25*f;
                }
                // Resource indicators are applied last so effects cannot obscure them.
                if(s.Team==Team.Player && s.Tactical && settings.Health && s.HasHealth && id>=65537 && id<=65542)
                {
                    int bottomIndex=6-(int)(id-65536);
                    double ratio=s.Health/s.MaxHealth;
                    double fill=Math.Max(0,Math.Min(1,ratio*6-bottomIndex));
                    rr=4+fill*(ratio<=.3?230:15);gg=3+fill*(ratio<=.3?18:180);bb=3+fill*12;
                }
                if(s.Team==Team.Player && s.Tactical && settings.ActionPoints && s.HasAP)
                {
                    if(id>=2 && id<=4){bool lit=s.AP>=id-1;rr=lit?255:8;gg=lit?185:5;bb=lit?8:2;}
                }
                if(s.Team==Team.Player && s.Tactical && settings.Advantage && (s.HasAdvantage || s.AdvantageReady==1))
                {
                    int slot=advantageSlots[i];
                    if(slot>=0 || id==112)
                    {
                        double fill=s.HasAdvantage?Math.Max(0,Math.Min(1,s.Advantage/s.MaxAdvantage*9-slot)):0;
                        if(id==112)fill=0;
                        if(s.AdvantageReady==1)
                        {
                            rr=230*pulse;gg=100*pulse;bb=255*pulse;
                        }
                        else {rr=8+fill*165;gg=2+fill*15;bb=10+fill*230;}
                    }
                }
                if(id==71 && s.Team==Team.Player && s.Tactical && settings.EndTurn)
                {
                    // Neutral/dim unless the entire living squad has known zero AP.
                    rr=s.EndTurnReady?255:8;gg=s.EndTurnReady?5:12;bb=s.EndTurnReady?2:16;
                }
                Colors[i].R=Clamp(rr);Colors[i].G=Clamp(gg);Colors[i].B=Clamp(bb);Colors[i].A=255;
            }
        }
        private static byte Clamp(double n){return (byte)Math.Max(0,Math.Min(255,n));}
    }
}
