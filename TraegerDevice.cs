using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraegerMon
{
    public class TraegerDevice
    {
        public string Name;
        public string ID;

        public int CurrentTemp;
        public int SetTemp;
        internal int ProbeTemp;
        internal int FanLevel;
        internal int Smoke;

        internal long CookTimerEnd;
        internal long SystemTimerEnd;

        internal int SystemStatus;
    }

    public class DataFrame
    {
        public DateTime Time;
        public double CurrentTemp;
        public double TargetTemp;
    }
}
