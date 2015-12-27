﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVVM.Component
{
    public class RelaySimpleCommand : ISimpleCommand
    {
        private Action _Do;

        public RelaySimpleCommand(Action iDo)
        {
            _Do = iDo;
        }

        public void Execute(object argument)
        {
            _Do();
        }
    }
}
