using System;
using System.Collections.Generic;

namespace LINQPad
{
    public interface ICustomMemberProvider
    {
        IEnumerable<string> GetNames();
        IEnumerable<Type> GetTypes();
        IEnumerable<object> GetValues();
    }
}