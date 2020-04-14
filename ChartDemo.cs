using System;
using System.Windows.Forms;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using Brushes = System.Windows.Media.Brushes;


namespace TraegerMon
{
    public class DateModel
    {
        public System.DateTime DateTime { get; set; }
        public double Value { get; set; }
    }
    public partial class ChartDemo : Form
    {
        int current;
        LineSeries Points;
        public ChartDemo()
        {
            InitializeComponent();

            
        }

        private void T_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Points.Values.Add(new DateModel() { DateTime = System.DateTime.Now, Value = ++current });
        }
    }
}
