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
using System.Reflection;
using System.Text;
using System.IO;

namespace IFS.IfsConsole
{
    /// <summary>
    /// Defines a node in the debug command tree.
    /// </summary>
    public class ConsoleCommand
    {
        public ConsoleCommand(string name, String description, String usage, MethodInfo method)
        {
            Name = name.Trim().ToLower();
            Description = description;
            Usage = usage;
            Methods = new List<MethodInfo>(4);

            if (method != null)
            {
                Methods.Add(method);
            }

            SubCommands = new List<ConsoleCommand>();
        }

        public string Name;
        public string Description;
        public string Usage;
        public List<MethodInfo> Methods;
        public List<ConsoleCommand> SubCommands;

        public override string ToString()
        {
            if (this.Methods.Count == 0)
            {
                return String.Format("{0}... ({1})", this.Name, this.SubCommands.Count);
            }
            else
            {
                return this.Name;
            }
        }

        public void AddSubNode(List<string> words, MethodInfo method)
        {
            // We should never hit this case.
            if (words.Count == 0)
            {
                throw new InvalidOperationException("Out of words building command node.");
            }

            // Check the root to see if a node for the first incoming word has already been added
            ConsoleCommand subNode = FindSubNodeByName(words[0]);

            if (subNode == null)
            {
                // No, it has not -- create one and add it now.
                subNode = new ConsoleCommand(words[0], null, null, null);
                this.SubCommands.Add(subNode);

                if (words.Count == 1)
                {
                    // This is the last stop -- set the method and be done with it now.
                    subNode.Methods.Add(method);

                    // early return.
                    return;
                }
            }
            else
            {
                // The node already exists, we will be adding a subnode, hopefully.
                if (words.Count == 1)
                {
                    //
                    // If we're on the last word at this point then this is an overloaded command.
                    // Check that we don't have any other commands with this number of arguments.
                    //
                    int argCount = method.GetParameters().Length;
                    foreach (MethodInfo info in subNode.Methods)
                    {
                        if (info.GetParameters().Length == argCount)
                        {
                            throw new InvalidOperationException("Duplicate overload for console command");
                        }
                    }

                    //
                    // We're ok.  Add it to the method list.
                    //
                    subNode.Methods.Add(method);

                    // and return early.
                    return;
                }
            }

            // We have more words to go.
            words.RemoveAt(0);
            subNode.AddSubNode(words, method);

        }

        public ConsoleCommand FindSubNodeByName(string name)
        {
            ConsoleCommand found = null;

            foreach (ConsoleCommand sub in SubCommands)
            {
                if (sub.Name == name)
                {
                    found = sub;
                    break;
                }
            }

            return found;
        }
    }
    
    public class ConsoleExecutor
    {
        private ConsoleExecutor()
        {
            BuildCommandTree();            

            _consolePrompt = new ConsolePrompt(_commandRoot);
        }
        
        public static ConsoleExecutor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConsoleExecutor();
                }

                return _instance;
            }
        }        
        
        public void Run()
        {
            bool exit = false;
            while (!exit)
            {
                try
                {
                    // Get the command string from the prompt.
                    string command = _consolePrompt.Prompt().Trim();

                    if (command != String.Empty)
                    {
                        exit = ExecuteLine(command);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }            
        }

        private bool ExecuteLine(string line)
        {
            bool exit = false;
                
            // Comments start with "#"
            if (line.StartsWith("#"))
            {
                // Do nothing, ignore.
            }                        
            else
            {
                string[] args = null;
                List<MethodInfo> methods = GetMethodsFromCommandString(line, out args);

                if (methods == null)
                {
                    // Not a command.
                    Console.WriteLine("Invalid command.");
                }
                else
                {
                    exit = InvokeConsoleMethod(methods, args);
                }
            }

            return exit;
        }

        private bool InvokeConsoleMethod(List<MethodInfo> methods, string[] args)
        {
            bool exit = false;
            MethodInfo method = null;

            //
            // Find the method that matches the arg count we were passed
            // (i.e. handle overloaded commands)
            //
            foreach (MethodInfo m in methods)
            {
                ParameterInfo[] paramInfo = m.GetParameters();

                if (args == null && paramInfo.Length == 0 ||
                    paramInfo.Length == args.Length)
                {
                    // found a match
                    method = m;
                    break;
                }
            }

            if (method == null)
            {
                // invalid argument count.                
                throw new ArgumentException(String.Format("Invalid argument count to command."));
            }

            ParameterInfo[] parameterInfo = method.GetParameters();
            object[] invokeParams;

            if (args == null)
            {
                invokeParams = null;
            }
            else
            {
                invokeParams = new object[parameterInfo.Length];
            }

            for (int i = 0; i < parameterInfo.Length; i++)
            {
                ParameterInfo p = parameterInfo[i];

                if (p.ParameterType.IsEnum)
                {
                    //
                    // This is an enumeration type.
                    // See if we can find an enumerant that matches the argument.
                    //
                    FieldInfo[] fields = p.ParameterType.GetFields();

                    foreach (FieldInfo f in fields)
                    {
                        if (!f.IsSpecialName && args[i].ToLower() == f.Name.ToLower())
                        {
                            invokeParams[i] = f.GetRawConstantValue();
                        }
                    }

                    if (invokeParams[i] == null)
                    {
                        // no match, provide possible values
                        StringBuilder sb = new StringBuilder(String.Format("Invalid value for parameter {0}.  Possible values are:", i));

                        foreach (FieldInfo f in fields)
                        {
                            if (!f.IsSpecialName)
                            {
                                sb.AppendFormat("{0} ", f.Name);
                            }
                        }

                        sb.AppendLine();

                        throw new ArgumentException(sb.ToString());
                    }

                }
                else if (p.ParameterType.IsArray)
                {
                    //
                    // If a function takes an array type, i should do something here, yeah.
                    //
                }
                else
                {
                    // must be something more normal...
                    if (p.ParameterType == typeof(uint))
                    {
                        invokeParams[i] = TryParseUint(args[i]);
                    }
                    else if (p.ParameterType == typeof(ushort))
                    {
                        invokeParams[i] = TryParseUshort(args[i]);
                    }
                    else if (p.ParameterType == typeof(string))
                    {
                        invokeParams[i] = args[i];
                    }
                    else if (p.ParameterType == typeof(char))
                    {
                        invokeParams[i] = (char)args[i][0];
                    }
                    else
                    {
                        throw new ArgumentException(String.Format("Unhandled type for parameter {0}, type {1}", i, p.ParameterType));
                    }
                }
            }

            //
            // If we've made it THIS far, then we were able to parse all the commands into what they should be.
            // Invoke the method on the static instance exposed by the class.
            //
            object instance = GetInstanceFromMethod(method);

            return (bool)method.Invoke(instance, invokeParams);        
        }
       
        private object GetInstanceFromMethod(MethodInfo method)
        {
            Type instanceType = method.DeclaringType;
            PropertyInfo property = instanceType.GetProperty("Instance");
            return property.GetValue(null, null);
        }

        enum ParseState
        {
            NonWhiteSpace = 0,
            WhiteSpace = 1,
            QuotedString = 2,
        }

        private List<string> SplitArgs(string commandString)
        {
            // We split on whitespace and specially handle quoted strings (quoted strings count as a single arg)
            //
            List<string> args = new List<string>();

            commandString = commandString.Trim();

            StringBuilder sb = new StringBuilder();

            ParseState state = ParseState.NonWhiteSpace;

            foreach(char c in commandString)
            {
                switch (state)
                {
                    case ParseState.NonWhiteSpace:
                        if (char.IsWhiteSpace(c))
                        {
                            // End of token
                            args.Add(sb.ToString());
                            sb.Clear();
                            state = ParseState.WhiteSpace;
                        }
                        else if (c == '\"')
                        {
                            // Start of quoted string
                            state = ParseState.QuotedString;
                        }
                        else
                        {
                            // Character in token
                            sb.Append(c);
                        }
                        break;

                    case ParseState.WhiteSpace:
                        if (!char.IsWhiteSpace(c))
                        {
                            // Start of new token
                            if (c != '\"')
                            {
                                sb.Append(c);
                                state = ParseState.NonWhiteSpace;
                            }
                            else
                            {
                                // Start of quoted string
                                state = ParseState.QuotedString;
                            }
                        }
                        break;

                    case ParseState.QuotedString:
                        if (c == '\"')
                        {
                            // End of quoted string.
                            args.Add(sb.ToString());
                            sb.Clear();
                            state = ParseState.WhiteSpace;
                        }
                        else
                        {
                            // Character in quoted string
                            sb.Append(c);
                        }
                        break;
                }                                   
            }

            if (sb.Length > 0)
            {
                // Add the last token to the args list
                args.Add(sb.ToString());
            }

            return args;
        }

        private List<MethodInfo> GetMethodsFromCommandString(string command, out string[] args)
        {
            args = null;

            List<string> cmdArgs = SplitArgs(command);                

            ConsoleCommand current = _commandRoot;
            int commandIndex = 0;

            while (true)
            {
                // If this node has an executor, then we're done
                // (We assume that the tree is correctly built and that only
                // leaves have executors)
                if (current.Methods.Count > 0)
                {
                    break;
                }

                if (commandIndex > cmdArgs.Count - 1)
                {
                    // Out of args!
                    return null;
                }

                // Otherwise we continue down the tree.
                current = current.FindSubNodeByName(cmdArgs[commandIndex]);
                commandIndex++;

                if (current == null)
                {
                    // If the node was not found, then the command is invalid.
                    return null;
                }
            }

            //Now current should point to the command with the executor
            //and commandIndex should point to the first argument to the command.

            cmdArgs.RemoveRange(0, commandIndex);

            args = cmdArgs.ToArray();
            return current.Methods;
        }

       

        private static uint TryParseUint(string arg)
        {
            uint result = 0;
            bool hexadecimal = false;

            //if args starts with a "0x" it's hex
            //otherwise assume decimal
            if (arg.StartsWith("0x"))
            {
                hexadecimal = true;

                //strip the "0x"
                arg = arg.Remove(0, 2);
            }

            try
            {
                result = uint.Parse(arg, hexadecimal ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer);
            }
            catch
            {
                Console.WriteLine("{0} was not a valid 32-bit decimal or hexadecimal constant.", arg);
                throw;
            }

            return result;
        }

        private static ushort TryParseUshort(string arg)
        {
            ushort result = 0;
            bool hexadecimal = false;

            //if args starts with a "0x" it's hex
            //otherwise assume decimal
            if (arg.StartsWith("0x"))
            {
                hexadecimal = true;

                //strip the "0x"
                arg = arg.Remove(0, 2);
            }

            try
            {
                result = ushort.Parse(arg, hexadecimal ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer);
            }
            catch
            {
                Console.WriteLine("{0} was not a valid 16-bit decimal or hexadecimal constant.", arg);
                throw;
            }

            return result;
        }       

        /// <summary>
        /// Builds the debugger command tree.
        /// </summary>
        private void BuildCommandTree()
        {         
            // Build the flat list which will be built into the tree, by walking
            // the classes that provide the methods
            _commandList = new List<ConsoleCommand>();

            Type[] commandTypes = {
                    typeof(ConsoleCommands),
                    typeof(ConsoleExecutor),
                    };

            foreach (Type type in commandTypes)
            {
                foreach (MethodInfo info in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object[] attribs = info.GetCustomAttributes(typeof(ConsoleFunction), true);

                    if (attribs.Length > 1)
                    {
                        throw new InvalidOperationException(String.Format("More than one ConsoleFunction attribute set on {0}", info.Name));
                    }
                    else if (attribs.Length == 1)
                    {
                        // we have a debugger attribute set on this method
                        // this cast should always succeed given that we're filtering for this type above.
                        ConsoleFunction function = (ConsoleFunction)attribs[0];

                        ConsoleCommand newCommand = new ConsoleCommand(function.CommandName, function.Description, function.Usage, info);

                        _commandList.Add(newCommand);
                    }
                }
            }

            // Now actually build the command tree from the above list!
            _commandRoot = new ConsoleCommand("Root", null, null, null);

            foreach (ConsoleCommand c in _commandList)
            {
                string[] commandWords = c.Name.Split(' ');

                // This is kind of ugly, we know that at this point every command built above have only
                // one method.  When building the tree, overloaded commands may end up with more than one.
                _commandRoot.AddSubNode(new List<string>(commandWords), c.Methods[0]);
            }
        }

        [ConsoleFunction("show commands", "Shows console commands and their descriptions.")]
        private bool ShowCommands()
        {
            foreach (ConsoleCommand cmd in _commandList)
            {
                if (!string.IsNullOrEmpty(cmd.Usage))
                {
                    Console.WriteLine("{0} - {1}\n  {2}\n", cmd.Name, cmd.Description, cmd.Usage);
                }
                else
                {
                    Console.WriteLine("{0} - {1}", cmd.Name, cmd.Description);
                }
            }

            return false;
        }

        private ConsolePrompt _consolePrompt;
        private ConsoleCommand _commandRoot;
        private List<ConsoleCommand> _commandList;

        private static ConsoleExecutor _instance;     
    }
}
