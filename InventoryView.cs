using System;
using System.Collections.Generic;
using GeniePlugin.Interfaces;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

namespace InventoryView
{
    public class InventoryView : GeniePlugin.Interfaces.IPlugin
    {
        // Constant variable for the Properties of the plugin
        // At the top for easy changes.
        readonly string _NAME = "InventoryView";
        readonly string _VERSION = "1.7";
        readonly string _AUTHOR = "Etherian <https://github.com/EtherianDR/InventoryView>";
        readonly string _DESCRIPTION = "Stores your character inventory and allows you to search items across characters.";

        // Required for plugin
        private static IHost _host;                             // To communicate back to Genie and the game.
        private static System.Windows.Forms.Form _parent;       // The parent form that renders this plugin.

        #region InventoryView Members

        // Used to pause the plugin. Players can enable/disable plugins from "Plugins > Plugins..." menu.
        private bool _enabled = true;

        // This contains all the of the inventory data.
        public static List<CharacterData> _characterData = new List<CharacterData>();

        // Path to Genie config.
        private static string _basePath = Application.StartupPath;

        // Whether or not InventoryView is currently scanning data, and what state it is in.
        private string _scanMode = null;

        // Keeps track of how many containers deep you are when scanning inventory in containers.
        private int _level = 1;

        // The current character & source being scanned.
        private CharacterData _currentData = null;

        // The last item tha was scanned.
        private ItemData _lastItem = null;

        private bool debug = false;
        private string lastText = "";

        #endregion
        #region IPlugin Properties

        // Required for Plugin - Called when Genie needs the name of the plugin (On menu)
        // Return Value:
        //              string: Text that is the name of the Plugin
        public string Name
        {
            get { return _NAME; }
        }

        // Required for Plugin - Called when Genie needs the plugin version (error text
        //                      or the plugins window)
        // Return Value:
        //              string: Text that is the version of the plugin
        public string Version
        {
            get { return _VERSION; }
        }

        // Required for Plugin - Called when Genie needs the plugin Author (plugins window)
        // Return Value:
        //              string: Text that is the Author of the plugin
        public string Author
        {
            get { return _AUTHOR; }
        }

        // Required for Plugin - Called when Genie needs the plugin Description (plugins window)
        // Return Value:
        //              string: Text that is the description of the plugin
        //                      This can only be up to 200 Characters long, else it will appear
        //                      "truncated"
        public string Description
        {
            get { return _DESCRIPTION; }
        }

        // Required for Plugin - Called when Genie needs disable/enable the plugin (Plugins window,
        //                      and from the CLI), or when Genie needs to know the status of the
        //                      plugin (???)
        // Get:
        //      Not Known what it is used for
        // Set:
        //      Used by Plugins Window + CLI
        public bool Enabled
        {
            get
            {
                return _enabled;
            }
            set
            {
                _enabled = value;
            }

        }

        #endregion
        #region IPlugin Methods

        public void Initialize(IHost host)
        {
            _host = host;

            _basePath = _host.get_Variable("PluginPath");

            // Load inventory from the XML config if available.
            LoadSettings(initial: true);
        }

        // Required for Plugin - This method is called when clicking on the plugin
        //                       name from the Menu item Plugins
        public void Show()
        {
            if (_parent == null || _parent.IsDisposed)
            {
                _parent = new InventoryViewForm();
            }

            _parent.Show();
        }

        // Required for Plugin - This method is called when a global variable in Genie is changed
        // Parameters:
        //              string Text:  The variable name in Genie that changed
        public void VariableChanged(string variable)
        {

        }

        // Required for Plugin -
        // Parameters:
        //              string Text:    The DIRECT text comes from the game (non-"xml")
        //              string Window:  The Window the Text was received from
        // Return Value:
        //              string: Text that will be sent to the main window
        public string ParseText(string text, string window)
        {
            //check to see if plugin is paused or not. If paused, just return the text back to Genie.
            if (!_enabled)
            {
                return text;
            }

            string characterName = _host.get_Variable("charactername");

            if (_scanMode != null) // If a scan isn't in progress, do nothing here.
            {
                string trimText = text.Trim(new char[] { '\n', '\r', ' ' }); // Trims spaces and newlines.
                lastText = trimText;
                if (trimText.StartsWith("XML") && trimText.EndsWith("XML"))
                {
                    // Skip XML parser lines
                }
                else if (string.IsNullOrEmpty(trimText))
                {
                    // Skip blank lines
                }
                else if (_scanMode == "Start") // When a scan is initiated, it starts here.
                {
                    if (trimText == "You have:") // Text that appears at the beginning of "inventory list"
                    {
                        _host.EchoText("Scanning Inventory.");
                        _scanMode = "Inventory";
                        _currentData = new CharacterData() { name = characterName, source = "Inventory" };
                        _characterData.Add(_currentData);
                        _level = 1;
                    }
                } // end if Start
                else if (_scanMode == "Inventory")
                {
                    if (text.StartsWith("[Use INVENTORY HELP"))
                    {
                        // Skip
                    }
                    else if (text.StartsWith("Roundtime:")) // text that appears at the end of "inventory list"
                    {
                        // Inventory List has a RT based on the number of items, so grab the number and pause the thread for that length.
                        Match match = Regex.Match(trimText, @"^Roundtime:\s{1,3}(\d{1,3})\s{1,3}secs?\.$");
                        _scanMode = "VaultStart";
                        _host.EchoText(string.Format("Pausing {0} seconds for RT.", int.Parse(match.Groups[1].Value)));
                        System.Threading.Thread.Sleep(int.Parse(match.Groups[1].Value) * 1000);
                        _host.SendText("get my vault book");
                    }
                    else
                    {
                        // The first _level of inventory has a padding of 2 spaces to the left, and each _level adds an additional 3 spaces.
                        // 2, 5, 8, 11, 14, etc..
                        int spaces = text.Length - text.TrimStart().Length;
                        int newlevel = (spaces + 1) / 3;
                        string tap = trimText;
                        // remove the - from the beginning if it exists.
                        if (tap.StartsWith("-")) tap = tap.Remove(0, 1);

                        // The logic below builds a tree of inventory items.
                        if (newlevel == 1) // If the item is in the first _level, add to the root item list
                        {
                            _lastItem = _currentData.AddItem(new ItemData() { tap = tap });
                        }
                        else if (newlevel == _level) // If this is the same _level as the previous item, add to the previous item's parent's item list.
                        {
                            _lastItem = _lastItem.parent.AddItem(new ItemData() { tap = tap });
                        }
                        else if (newlevel == _level + 1) // If this item is down a _level from the previous, add it to the previous item's item list.
                        {
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        else // Else, if the item is up a _level, loop back until you reach the correct _level.
                        {
                            for (int i = newlevel; i <= _level; i++)
                            {
                                _lastItem = _lastItem.parent;
                            }
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        _level = newlevel;
                    }
                } // end if Inventory
                else if (_scanMode == "VaultStart")
                {
                    // Get the vault book & read it.
                    Match match = Regex.Match(trimText, @"^You get a.*vault book.*from");
                    if (match.Success || trimText == "You are already holding that.")
                    {
                        _host.EchoText("Scanning Vault.");
                        _host.SendText("read my vault book");
                    }
                    else if (trimText == "Vault Inventory:") // This text appears at the beginning of the vault list.
                    {
                        _scanMode = "Vault";
                        _currentData = new CharacterData() { name = characterName, source = "Vault" };
                        _characterData.Add(_currentData);
                        _level = 1;
                    }
                    // If you don't have a vault book or you can't read a vault book, it skips to checking your deed register.
                    else if (trimText == "You currently do not have a vault rented." || trimText == "What were you referring to?" || trimText == "The script that the vault book is written in is unfamiliar to you.  You are unable to read it." || trimText == "The vault book is filled with blank pages pre-printed with branch office letterhead.  An advertisement touting the services of Rundmolen Bros. Storage Co. is pasted on the inside cover.")
                    {
                        _host.EchoText("Skipping Vault.");
                        _scanMode = "DeedStart";
                        _host.SendText("get my deed register");
                    }
                } // end if VaultStart
                else if (_scanMode == "Vault")
                {
                    // This text indicates the end of the vault inventory list.
                    if (text.StartsWith("The last note in your book indicates that your vault contains"))
                    {
                        _scanMode = "DeedStart";
                        _host.SendText("stow my vault book");
                        _host.SendText("get my deed register");
                    }
                    else
                    {
                        // Determine how many levels down an item is based on the number of spaces before it.
                        // Anything greater than 4 levels down shows up at the same _level as its parent.
                        int spaces = text.Length - text.TrimStart().Length;
                        int newlevel = 1;
                        if (spaces > 4)
                        {
                            newlevel += (spaces - 4) / 2;
                        }
                        string tap = trimText;
                        if (tap.StartsWith("-"))
                        {
                            tap = tap.Remove(0, 1);
                        }
                        if (newlevel == 1)
                        {
                            _lastItem = _currentData.AddItem(new ItemData() { tap = tap, storage = true });
                        }
                        else if (newlevel == _level)
                        {
                            _lastItem = _lastItem.parent.AddItem(new ItemData() { tap = tap });
                        }
                        else if (newlevel == _level + 1)
                        {
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        else
                        {
                            for (int i = newlevel; i <= _level; i++)
                            {
                                _lastItem = _lastItem.parent;
                            }
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        _level = newlevel;
                    }
                } // end if Vault
                else if (_scanMode == "DeedStart")
                {
                    // Get the vault book & read it.
                    Match match = Regex.Match(trimText, @"^You get a.*deed register.*from");
                    if (match.Success || trimText == "You are already holding that.")
                    {
                        _host.EchoText("Scanning Deed Register.");
                        _host.SendText("turn my deed register to contents");
                        _host.SendText("read my deed register");
                    }
                    else if (trimText == "Page -- Deed") // This text appears at the beginning of the deed register list.
                    {
                        _scanMode = "Deed";
                        _currentData = new CharacterData() { name = characterName, source = "Deed" };
                        _characterData.Add(_currentData);
                        _level = 1;
                    }
                    // If you don't have a deed register or it is empty, it skips to checking your house.
                    else if (trimText == "What were you referring to?" || trimText.StartsWith("You haven't stored any deeds in this register."))
                    {
                        _host.EchoText("Skipping Deed Register.");
                        _scanMode = "HomeStart";
                        _host.SendText("home recall");
                    }
                } // end if DeedStart
                else if (_scanMode == "Deed")
                {
                    if (trimText.StartsWith("Currently stored"))
                    {
                        _host.SendText("stow my deed register");
                        _scanMode = "HomeStart";
                        _host.SendText("home recall");
                    }
                    else
                    {
                        string tap = trimText.Substring(trimText.IndexOf("--") + 3);
                        _lastItem = _currentData.AddItem(new ItemData() { tap = tap, storage = false });
                    }
                } // end if Deed
                else if (_scanMode == "HomeStart")
                {
                    if (trimText == "The home contains:") // This text appears at the beginning of the home list.
                    {
                        _host.EchoText("Scanning Home.");
                        _scanMode = "Home";
                        _currentData = new CharacterData() { name = characterName, source = "Home" };
                        _characterData.Add(_currentData);
                        _level = 1;
                    }
                    // This text appears if you don't have a home, skips and saves the results.
                    else if (trimText.StartsWith("Your documentation filed with the Estate Holders"))
                    {
                        _host.EchoText("Skipping Home.");
                        if (_host.get_Variable("guild") == "Trader")
                        {
                            _scanMode = "TraderStart";
                            _host.SendText("get my storage book");
                        }
                        else
                        {
                            _scanMode = null;
                            _host.EchoText("Scan Complete.");
                            _host.SendText("#parse InventoryView scan complete");
                            SaveSettings();
                        }
                    }
                    else if (trimText == "You shouldn't do that while inside of a home.  Step outside if you need to check something.")
                    {
                        _host.EchoText("You cannot check the contents of your home while inside of a home. Step outside and try again.");
                        if (_host.get_Variable("guild") == "Trader")
                        {
                            _scanMode = "TraderStart";
                            _host.SendText("get my storage book");
                        }
                        else
                        {
                            _scanMode = null;
                            _host.EchoText("Scan Complete.");
                            _host.SendText("#parse InventoryView scan complete");
                            SaveSettings();
                        }
                    }
                } // end if HomeStart
                else if (_scanMode == "Home")
                {
                    if (trimText == ">") // There is no text after the home list, so watch for the next >
                    {
                        if (_host.get_Variable("guild") == "Trader")
                        {
                            _scanMode = "TraderStart";
                            _host.SendText("get my storage book");
                        }
                        else
                        {
                            _scanMode = null;
                            _host.EchoText("Scan Complete.");
                            _host.SendText("#parse InventoryView scan complete");
                            SaveSettings();
                        }
                    }
                    else if (trimText.StartsWith("Attached:")) // If the item is attached, it is in/on/under/behind a piece of furniture.
                    {
                        string tap = trimText.Replace("Attached: ", "");
                        _lastItem = (_lastItem.parent != null ? _lastItem.parent : _lastItem ).AddItem(new ItemData() { tap = tap });
                    }
                    else // Otherwise, it is a piece of furniture.
                    {
                        string tap = trimText.Substring(trimText.IndexOf(":")+2);
                        _lastItem = _currentData.AddItem(new ItemData() { tap = tap, storage = true });
                    }
                } // end if Home
                else if (_scanMode == "TraderStart")
                {
                    // Get the storage book & read it.
                    Match match = Regex.Match(trimText, @"^You get a.*storage book.*from");
                    if (match.Success || trimText == "You are already holding that.")
                    {
                        _host.EchoText("Scanning Trader Storage.");
                        _host.SendText("read my storage book");
                    }
                    else if (trimText == "in the known realms since 402.") // This text appears at the beginning of the storage book list.
                    {
                        _scanMode = "Trader";
                        _currentData = new CharacterData() { name = characterName, source = "TraderStorage" };
                        _characterData.Add(_currentData);
                        _level = 1;
                    }
                    // If you don't have a vault book or you can't read a vault book, it skips to checking your house.
                    else if (trimText == "What were you referring to?" || trimText == "The storage book is filled with complex lists of inventory that make little sense to you.")
                    {
                        _scanMode = null;
                        _host.EchoText("Skipping Trader Storage.");
                        _host.EchoText("Scan Complete.");
                        _host.SendText("#parse InventoryView scan complete");
                        SaveSettings();
                    }
                } // end if trader start
                else if (_scanMode == "Trader")
                {
                    // This text indicates the end of the vault inventory list.
                    if (text.StartsWith("A notation at the bottom indicates"))
                    {
                        _scanMode = null;
                        _host.EchoText("Scan Complete.");
                        _host.SendText("#parse InventoryView scan complete");
                        SaveSettings();
                    }
                    else
                    {
                        // Determine how many levels down an item is based on the number of spaces before it.
                        // Anything greater than 4 levels down shows up at the same _level as its parent.
                        int spaces = text.Length - text.TrimStart().Length;
                        int newlevel = 1;
                        switch (spaces)
                        {
                            case 4:
                                newlevel = 1;
                                break;
                            case 8:
                                newlevel = 2;
                                break;
                            case 12:
                                newlevel = 3;
                                break;
                           default:
                                newlevel = 3;
                                break;
                        }
                        string tap = trimText;
                        if (tap.StartsWith("-")) tap = tap.Remove(0, 1);
                        if (newlevel == 1)
                        {
                            _lastItem = _currentData.AddItem(new ItemData() { tap = tap, storage = true });
                        }
                        else if (newlevel == _level)
                        {
                            _lastItem = _lastItem.parent.AddItem(new ItemData() { tap = tap });
                        }
                        else if (newlevel == _level + 1)
                        {
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        else
                        {
                            for (int i = newlevel; i <= _level; i++)
                            {
                                _lastItem = _lastItem.parent;
                            }
                            _lastItem = _lastItem.AddItem(new ItemData() { tap = tap });
                        }
                        _level = newlevel;
                    }
                } // end if Trader
            }

            return text;
        }

        // Required for Plugin - Called when user enters text in the command box
        // Parameters:
        //              string Text:  The text the user entered in the command box
        // Return Value:
        //              string: Text that will be sent to the game
        public string ParseInput(string text)
        {
            // Check to see if plugin is paused or not. If paused, just return the text back to Genie.
            if (!_enabled)
            {
                return text;
            }

            string characterName = _host.get_Variable("charactername");

            if (text.ToLower().StartsWith("/inventoryview ") || text.ToLower().StartsWith("/iv "))
            {
                var splitText = text.Split(' ');
                if (splitText.Length == 1 || splitText[1].ToLower() == "help")
                {
                    Help();
                }
                else if (splitText[1].ToLower() == "scan")
                {
                    if (_host.get_Variable("connected") == "0")
                    {
                        _host.EchoText("You must be connected to the server to do a scan.");
                    }
                    else
                    {
                        LoadSettings();
                        _scanMode = "Start";
                        _characterData.RemoveAll(tbl => tbl.name == characterName);
                        _host.SendText("inventory list");
                    }
                }
                else if (splitText[1].ToLower() == "open")
                {
                    Show();
                }
                else if (splitText[1].ToLower() == "debug")
                {
                    debug = !debug;
                    _host.EchoText("InventoryView debug Mode " + (debug ? "ON" : "OFF"));
                }
                else if (splitText[1].ToLower() == "lasttext")
                {
                    debug = !debug;
                    _host.EchoText("InventoryView debug Last Text: " + lastText);
                }
                else
                {
                    Help();
                }

                return string.Empty;
            }
            return text;
        }

        // Required for Plugin -
        // Parameters:
        //              string Text:  That "xml" text comes from the game
        public void ParseXML(string xml)
        {

        }

        // Required for Plugin - Might indicate Genie is closing?
        public void ParentClosing()
        {

        }

        #endregion
        #region Custom Parse/Display methods

        public static void Help()
        {
            _host.EchoText("Inventory View plugin options:");
            _host.EchoText("/InventoryView scan  -- scan the items on the current character.");
            _host.EchoText("/InventoryView open  -- open the InventoryView Window to see items.");
        }

        public static void RemoveParents(List<ItemData> iList)
        {
            foreach (var iData in iList)
            {
                iData.parent = null;
                RemoveParents(iData.items);
            }
        }

        public static void AddParents(List<ItemData> iList, ItemData parent)
        {
            foreach (var iData in iList)
            {
                iData.parent = parent;
                AddParents(iData.items, iData);
            }
        }

        // Called by the form to build UI of inventory grouped by character.
        public static List<string> GetCharacterNames()
        {
            List<string> names = InventoryView._characterData.Select(tbl => tbl.name).Distinct().ToList();
            names.Sort();
            return names;
        }

        // Called by form to build a UI of inventory grouped by character.
        public static IEnumerable<CharacterData> GetCharacterInventory(string character)
        {
            List<CharacterData> data = InventoryView._characterData.Where(tbl => tbl.name == character).ToList();
            return data;
        }

        // Expose a way for the form to communicate back to Genie and the game
        // without exposing the _host internal details.
        public static void SendText(string text)
        {
            _host.SendText(text);
        }

        // Expose a way for the form to communicate back to Genie and the game
        // without exposing the _host internal details.
        public static void EchoText(string text)
        {
            _host.EchoText(text);
        }

        public static void LoadSettings(bool initial = false)
        {
            string configFile = Path.Combine(_basePath, "InventoryView.xml");
            if (File.Exists(configFile))
            {
                try
                {
                    using (Stream stream = File.Open(configFile, FileMode.Open))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(List<CharacterData>));
                        _characterData = (List<CharacterData>)serializer.Deserialize(stream);
                    }
                    foreach (var cData in _characterData)
                    {
                        AddParents(cData.items, null);
                    }
                    if (!initial)
                        _host.EchoText("InventoryView data loaded.");
                }
                catch (IOException ex)
                {
                    _host.EchoText("Error reading InventoryView file: " + ex.Message);
                }
            }
        }

        public static void SaveSettings()
        {
            string configFile = Path.Combine(_basePath, "InventoryView.xml");
            try
            {
                // Can't serialize a class with circular references, so I have to remove the parent links first.
                foreach (var cData in _characterData)
                {
                    RemoveParents(cData.items);
                }
                FileStream writer = new FileStream(configFile, FileMode.Create);
                XmlSerializer serializer = new XmlSerializer(typeof(List<CharacterData>));
                serializer.Serialize(writer, _characterData);
                writer.Close();

                // ..and add them back again afterwards.
                foreach (var cData in _characterData)
                {
                    AddParents(cData.items, null);
                }
            }
            catch (IOException ex)
            {
                _host.EchoText("Error writing to InventoryView file: " + ex.Message);
            }
        }

        #endregion
    }
}