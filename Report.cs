using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RepairTasks
{
    class Report
    {
        public string Text { get; set; }
        public object Tag { get; set; }

        public Report(string text, object tag = null)
        {
            Text = text;
            Tag = tag;
        }
    }
}
