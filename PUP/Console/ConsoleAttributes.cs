/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

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
