using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    [Flags]
    internal enum GlobalHotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Windows = 0x0008,
        NoRepeat = 0x4000
    }

    internal sealed class GlobalHotkeyDefinition
    {
        public GlobalHotkeyModifiers Modifiers { get; private set; }
        public Keys Key { get; private set; }

        public static bool TryParse(string text, out GlobalHotkeyDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
            var modifiers = GlobalHotkeyModifiers.None;
            var key = Keys.None;
            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= GlobalHotkeyModifiers.Control;
                        break;
                    case "alt":
                        modifiers |= GlobalHotkeyModifiers.Alt;
                        break;
                    case "shift":
                        modifiers |= GlobalHotkeyModifiers.Shift;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= GlobalHotkeyModifiers.Windows;
                        break;
                    default:
                        if (key != Keys.None)
                        {
                            return false;
                        }
                        key = ParseKey(part);
                        break;
                }
            }

            var candidate = new GlobalHotkeyDefinition { Modifiers = modifiers, Key = key };
            if (!candidate.IsValid)
            {
                return false;
            }
            definition = candidate;
            return true;
        }

        public static string FromKeys(Keys keyData)
        {
            var parts = new List<string>();
            if ((keyData & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            if ((keyData & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            if (IsWindowsKeyPressed()) parts.Add("Win");

            var key = keyData & Keys.KeyCode;
            if (key != Keys.None)
            {
                parts.Add(KeyToText(key));
            }
            return string.Join("+", parts);
        }

        public static bool IsModifierOnly(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.LWin || key == Keys.RWin;
        }

        public static bool IsSingleModifierHotkey(string text)
        {
            GlobalHotkeyDefinition definition;
            return TryParse(text, out definition) && ModifierCount(definition.Modifiers) == 1;
        }

        private bool IsValid
        {
            get
            {
                return Key != Keys.None && IsAllowedBaseKey(Key) &&
                    IsAllowedModifierCombination(Key, Modifiers) &&
                    !IsReservedCombination(Key, Modifiers);
            }
        }

        private static bool IsAllowedModifierCombination(Keys key, GlobalHotkeyModifiers modifiers)
        {
            var count = ModifierCount(modifiers);
            if (count >= 2)
            {
                return true;
            }
            if (count != 1)
            {
                return false;
            }
            if (key >= Keys.F1 && key <= Keys.F24)
            {
                return true;
            }
            if ((modifiers & GlobalHotkeyModifiers.Shift) != 0)
            {
                return false;
            }
            return key == Keys.Oem5 || key == Keys.Oem3;
        }

        private static int ModifierCount(GlobalHotkeyModifiers modifiers)
        {
            var count = 0;
            if ((modifiers & GlobalHotkeyModifiers.Control) != 0) count++;
            if ((modifiers & GlobalHotkeyModifiers.Alt) != 0) count++;
            if ((modifiers & GlobalHotkeyModifiers.Shift) != 0) count++;
            if ((modifiers & GlobalHotkeyModifiers.Windows) != 0) count++;
            return count;
        }

        private static bool IsAllowedBaseKey(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z) ||
                (key >= Keys.D0 && key <= Keys.D9) ||
                (key >= Keys.NumPad0 && key <= Keys.NumPad9) ||
                (key >= Keys.F1 && key <= Keys.F24) ||
                key == Keys.Oem5 || key == Keys.Oem3 || key == Keys.Oem1 ||
                key == Keys.Oem7 || key == Keys.Oemcomma || key == Keys.OemPeriod ||
                key == Keys.OemQuestion || key == Keys.OemMinus || key == Keys.Oemplus ||
                key == Keys.OemOpenBrackets || key == Keys.Oem6;
        }

        private static bool IsReservedCombination(Keys key, GlobalHotkeyModifiers modifiers)
        {
            if ((modifiers & GlobalHotkeyModifiers.Alt) != 0 && key == Keys.F4)
            {
                return true;
            }
            if ((modifiers & GlobalHotkeyModifiers.Control) != 0 && key == Keys.Escape)
            {
                return true;
            }
            if ((modifiers & GlobalHotkeyModifiers.Windows) == 0)
            {
                return false;
            }
            if ((key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9))
            {
                return true;
            }
            return key == Keys.A || key == Keys.D || key == Keys.E || key == Keys.I ||
                key == Keys.L || key == Keys.M || key == Keys.R || key == Keys.S ||
                key == Keys.V || key == Keys.X;
        }

        private static bool IsWindowsKeyPressed()
        {
            return IsKeyPressed(GlobalHotkeyNativeMethods.LeftWindowsKey) ||
                IsKeyPressed(GlobalHotkeyNativeMethods.RightWindowsKey);
        }

        private static bool IsKeyPressed(int virtualKey)
        {
            return (GlobalHotkeyNativeMethods.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        private static Keys ParseKey(string text)
        {
            switch (text)
            {
                case "\\": return Keys.Oem5;
                case "`": return Keys.Oem3;
                case ";": return Keys.Oem1;
                case "'": return Keys.Oem7;
                case ",": return Keys.Oemcomma;
                case ".": return Keys.OemPeriod;
                case "/": return Keys.OemQuestion;
                case "-": return Keys.OemMinus;
                case "=": return Keys.Oemplus;
                case "[": return Keys.OemOpenBrackets;
                case "]": return Keys.Oem6;
            }

            Keys parsed;
            if (Enum.TryParse(text, true, out parsed))
            {
                return parsed;
            }
            if (text.Length == 1)
            {
                var character = char.ToUpperInvariant(text[0]);
                if (character >= 'A' && character <= 'Z')
                {
                    return (Keys)Enum.Parse(typeof(Keys), character.ToString(CultureInfo.InvariantCulture));
                }
                if (character >= '0' && character <= '9')
                {
                    return (Keys)Enum.Parse(typeof(Keys), "D" + character.ToString(CultureInfo.InvariantCulture));
                }
            }
            return Keys.None;
        }

        private static string KeyToText(Keys key)
        {
            switch (key)
            {
                case Keys.Oem5: return "\\";
                case Keys.Oem3: return "`";
                case Keys.Oem1: return ";";
                case Keys.Oem7: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.Oem6: return "]";
                default:
                    if (key >= Keys.D0 && key <= Keys.D9)
                    {
                        return ((char)('0' + (key - Keys.D0))).ToString(CultureInfo.InvariantCulture);
                    }
                    return key.ToString();
            }
        }
    }

    internal static class GlobalHotkeyNativeMethods
    {
        internal const int HotkeyMessage = 0x0312;
        internal const int LeftWindowsKey = 0x5B;
        internal const int RightWindowsKey = 0x5C;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, GlobalHotkeyModifiers modifiers, Keys key);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);
    }
}
