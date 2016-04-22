using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.IfsConsole
{   
    public class ConsoleFunction : Attribute
    {
        public ConsoleFunction(string commandName)
        {
            _commandName = commandName;
            _usage = "<No help available>";
        }

        public ConsoleFunction(string commandName, string description)
        {
            _commandName = commandName;
            _description = description;
        }

        public ConsoleFunction(string commandName, string description, string usage)
        {
            _commandName = commandName;
            _description = description;
            _usage = usage;
        }

        public string CommandName
        {
            get { return _commandName; }
        }

        public string Usage
        {
            get { return _usage; }
        }

        public string Description
        {
            get { return _description; }
        }

        private string _commandName;
        private string _description;
        private string _usage;
    }
}
