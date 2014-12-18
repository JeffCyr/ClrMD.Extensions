using System;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.LINQPad
{
    public static class LinqPadExtensions
    {
        public static bool SmartNavigation { get; set; }
        public static bool DisplayReferencedByField { get; set; }

        static LinqPadExtensions()
        {
            SmartNavigation = true;
            DisplayReferencedByField = true;
        }
    }
}
