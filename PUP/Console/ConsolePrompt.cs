using System;
using System.Collections.Generic;
using System.Text;

namespace IFS.IfsConsole
{
    public class ConsolePrompt
    {

        public ConsolePrompt(ConsoleCommand root)
        {
            _commandTree = root;
            _commandHistory = new List<string>(64);
            _historyIndex = 0;
        }

        /// <summary>
        /// Runs a nifty interactive debugger prompt.        
        /// </summary>
        public string Prompt()
        {
            DisplayPrompt();
            ClearInput();
            UpdateOrigin();

            bool entryDone = false;

            while (!entryDone)
            {
                UpdateDisplay();

                // Read one keystroke from the console...
                ConsoleKeyInfo key = Console.ReadKey(true);

                //Parse special chars...
                switch (key.Key)
                {
                    case ConsoleKey.Escape: //Clear input 
                        ClearInput();
                        break;

                    case ConsoleKey.Backspace: // Delete last char
                        DeleteCharAtCursor(true /* backspace */);
                        break;

                    case ConsoleKey.Delete: //Delete character at cursor
                        DeleteCharAtCursor(false /* delete */);
                        break;

                    case ConsoleKey.LeftArrow:
                        MoveLeft();
                        break;

                    case ConsoleKey.RightArrow:
                        MoveRight();
                        break;

                    case ConsoleKey.UpArrow:
                        HistoryPrev();
                        break;

                    case ConsoleKey.DownArrow:
                        HistoryNext();
                        break;

                    case ConsoleKey.Home:
                        MoveToBeginning();
                        break;

                    case ConsoleKey.End:
                        MoveToEnd();
                        break;

                    case ConsoleKey.Tab:
                        DoCompletion(false /* silent */);
                        break;

                    case ConsoleKey.Enter:
                        DoCompletion(true /* silent */);
                        UpdateDisplay();
                        CRLF();
                        entryDone = true;
                        break;

                    case ConsoleKey.Spacebar:
                        if (!_input.EndsWith(" ") &&
                            DoCompletion(true /* silent */))
                        {
                            UpdateDisplay();
                        }
                        else
                        {
                            InsertChar(key.KeyChar);
                        }
                        break;

                    default:
                        // Not a special key, just insert it if it's deemed printable.
                        if (char.IsLetterOrDigit(key.KeyChar) ||
                            char.IsPunctuation(key.KeyChar) ||
                            char.IsSymbol(key.KeyChar) ||
                            char.IsWhiteSpace(key.KeyChar))
                        {
                            InsertChar(key.KeyChar);
                        }
                        break;

                }
            }

            // Done.  Add to history if input is non-empty
            if (_input != string.Empty)
            {
                _commandHistory.Add(_input);
                HistoryIndex = _commandHistory.Count - 1;
            }

            return _input;
        }

        private void DeleteCharAtCursor(bool backspace)
        {
            if (_input.Length == 0)
            {
                //nothing to delete, bail.
                return;
            }

            if (backspace)
            {
                if (TextPosition == 0)
                {
                    // We's at the beginning of the input,
                    // can't backspace from here.
                    return;
                }
                else
                {
                    // remove 1 char at the position before the cursor.
                    // and move the cursor back one char
                    _input = _input.Remove(TextPosition - 1, 1);

                    TextPosition--;
                }
            }
            else
            {
                if (TextPosition == _input.Length)
                {
                    // At the end of input, can't delete a char from here
                    return;
                }
                else
                {
                    // remove one char at the current cursor pos.
                    // do not move the cursor
                    _input = _input.Remove(TextPosition, 1);
                }
            }
        }

        private void DisplayPrompt()
        {
            Console.Write(">");
        }

        private void UpdateDisplay()
        {
            //if the current input string is shorter than the last, then we need to erase a few chars at the end.
            string clear = String.Empty;

            if (_input.Length < _lastInputLength)
            {
                StringBuilder sb = new StringBuilder(_lastInputLength - _input.Length);
                for (int i = 0; i < _lastInputLength - _input.Length; i++)
                {
                    sb.Append(' ');
                }

                clear = sb.ToString();
            }

            int column = ((_textPosition + _originColumn) % Console.BufferWidth);
            int row = ((_textPosition + _originColumn) / Console.BufferWidth) + _originRow;

            // Move cursor to origin to draw string
            Console.CursorLeft = _originColumn;
            Console.CursorTop = _originRow;
            Console.Write(_input + clear);

            // Move cursor to text position to draw cursor
            Console.CursorLeft = column;
            Console.CursorTop = row;
            Console.CursorVisible = true;

            _lastInputLength = _input.Length;
        }

        private void MoveLeft()
        {
            TextPosition--;
        }

        private void MoveRight()
        {
            TextPosition++;
        }

        private void HistoryPrev()
        {

            HistoryIndex--;
            if (HistoryIndex < _commandHistory.Count)
            {
                _input = _commandHistory[HistoryIndex];
                TextPosition = _input.Length;
            }
        }

        private void HistoryNext()
        {
            HistoryIndex++;
            if (HistoryIndex < _commandHistory.Count)
            {
                _input = _commandHistory[HistoryIndex];
                TextPosition = _input.Length;
            }
        }

        private void MoveToBeginning()
        {
            TextPosition = 0;
        }

        private void MoveToEnd()
        {
            TextPosition = _input.Length;
        }

        private void ClearInput()
        {
            _input = String.Empty;
            TextPosition = 0;
        }

        private void InsertChar(char c)
        {
            _input = _input.Insert(TextPosition, c.ToString());
            TextPosition++;
        }

        private void CRLF()
        {
            Console.WriteLine();
        }

        private bool DoCompletion(bool silent)
        {
            // This code should probably move to another class, but hey, I'm lazy.

            // Take the current input and see if it matches anything in the command tree
            string[] tokens = _input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Save off the current cursor row; this is an ugly-ish hack to detect whether the match process
            // output anything.  If the cursor row changes then we'll need to move the prompt
            int oldRow = Console.CursorTop;

            string matchString = FuzzyMatch(_commandTree, new List<string>(tokens), silent);

            bool changed = false;

            if (matchString != String.Empty)
            {
                changed = _input.Trim().ToLower() != matchString.Trim().ToLower();

                _input = matchString;
                TextPosition = _input.Length;
            }

            if (!silent && oldRow != Console.CursorTop)
            {
                DisplayPrompt();
                UpdateOrigin();
            }

            return changed;
        }

        private string FuzzyMatch(ConsoleCommand root, List<string> tokens, bool silent)
        {
            if (tokens.Count == 0)
            {
                if (!silent)
                {
                    // If there are no tokens, just show the completion for the root.
                    PrintCompletions(root.SubCommands);
                }
                return String.Empty;
            }

            ConsoleCommand match = null;
            bool exactMatch = false;

            // Search for exact matches.  If we find one it's guaranteed to be unique
            // so we can follow that node.
            foreach (ConsoleCommand c in root.SubCommands)
            {
                if (c.Name.ToLower() == tokens[0].ToLower())
                {
                    match = c;
                    exactMatch = true;
                    break;
                }
            }

            if (match == null)
            {
                // No exact match.  Try a substring match.
                // If we have an unambiguous match then we can complete it automatically.
                // If the match is ambiguous, display possible completions and return String.Empty.
                List<ConsoleCommand> completions = new List<ConsoleCommand>();

                foreach (ConsoleCommand c in root.SubCommands)
                {
                    if (c.Name.StartsWith(tokens[0], StringComparison.InvariantCultureIgnoreCase))
                    {
                        completions.Add(c);
                    }
                }

                if (completions.Count == 1)
                {
                    // unambiguous match.  use it.
                    match = completions[0];
                }
                else if (completions.Count > 1)
                {
                    // ambiguous match.  display possible completions.
                    if (!silent)
                    {
                        PrintCompletions(completions);
                    }
                }
            }

            if (match == null)
            {
                // If we reach this point then no matches are available.  return the tokens we have...
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (i < tokens.Count - 1)
                    {
                        sb.AppendFormat("{0} ", tokens[i]);
                    }
                    else
                    {
                        sb.AppendFormat("{0}", tokens[i]);
                    }
                }

                return sb.ToString();
            }
            else
            {
                // A match was found
                tokens.RemoveAt(0);

                string subMatch = String.Empty;

                if (tokens.Count > 0)
                {
                    subMatch = FuzzyMatch(match, tokens, silent);
                }
                else if (exactMatch)
                {
                    if (!silent && match.SubCommands.Count > 0)
                    {
                        // Just show the completions for this node.
                        PrintCompletions(match.SubCommands);
                    }
                }

                if (subMatch == String.Empty)
                {
                    return String.Format("{0} ", match.Name);
                }
                else
                {
                    return String.Format("{0} {1}", match.Name, subMatch);
                }
            }
        }

        private void PrintCompletions(List<ConsoleCommand> completions)
        {
            // Just print all available completions at this node
            Console.WriteLine();
            Console.WriteLine("Possible completions are:");
            foreach (ConsoleCommand c in completions)
            {
                Console.Write("{0}\t", c);
            }
            Console.WriteLine();
        }

        private void UpdateOrigin()
        {
            _originRow = Console.CursorTop;
            _originColumn = Console.CursorLeft;
        }

        private int TextPosition
        {
            get
            {
                return _textPosition;
            }

            set
            {
                // Clip input between 0 and the length of input (+1, to allow adding text at end)
                _textPosition = Math.Max(0, value);
                _textPosition = Math.Min(_textPosition, _input.Length);
            }
        }

        private int HistoryIndex
        {
            get
            {
                return _historyIndex;
            }

            set
            {
                _historyIndex = Math.Min(_commandHistory.Count - 1, value);
                _historyIndex = Math.Max(0, _historyIndex);

            }
        }

        private ConsoleCommand _commandTree;


        private int _originRow;
        private int _originColumn;

        private string _input;
        private int _textPosition;
        private int _lastInputLength;

        private List<string> _commandHistory;
        private int _historyIndex;
    }
}
