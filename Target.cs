// Copyright (c) 2015, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RepairTasks
{
    class Target
    {
        public string RelativePath { get; set; }
        public string Name { get; set; }
        public string FullName { get { return RelativePath + Name; } }
    }
}
