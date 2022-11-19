using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using System.Text;

namespace ACT_Plugin
{
    /*
    * See https://github.com/EQAditu/AdvancedCombatTracker/wiki/Plugin-Creation-Tips
    */
    public class WhatRune : IActPluginV1
    {
#if DEBUG
        public static bool DEBUG = true;
#else
        public static bool DEBUG = false;
#endif

        public const int PLUGIN_ID = int.MaxValue;
        private const int DELAY_INIT_SECONDS = 2 * 1000;
        private const string MACRO_FILENAME = "whatrune.txt";
        public const string RUNES_DEFS = "https://raw.githubusercontent.com/eq2reapp/ActWhatRune/main/runes.txt";
        public const string HELP_PAGE = "https://github.com/eq2reapp/ActWhatRune/wiki/Help";

        public static string CONSIDER_TOKEN = "You consider";
        public static Regex REGEX_CONSIDER = new Regex(@"^(\\#[ABCDEF0-9]{6})?" + CONSIDER_TOKEN);
        public static string RUNE_TOKEN = "'rune ";
        public static Regex REGEX_RUNE = new Regex(@"^Unknown command: " + RUNE_TOKEN);

        private WhatRuneSettings _settings = null;
        private TabPage _pluginScreenSpace = null;
        private Label _pluginStatusText = null;
        private Button _btnFetchDefs = null;
        private TextBox _textBoxMacro = null;
        private TextBox _textBoxLogs = null;
        private List<String> Logs = new List<string>();
        private ConcurrentDictionary<string, string> Runes = new ConcurrentDictionary<string, string>();
        private Timer _timerFetchRunes = new Timer();

        public WhatRune()
        {
            _timerFetchRunes.Tick += _timerFetchRunes_Tick;
        }

        public void DeInitPlugin()
        {
            Log("Deinitializing plugin");
            ActGlobals.oFormActMain.OnLogLineRead -= oFormActMain_OnLogLineRead;
            ActGlobals.oFormActMain.UpdateCheckClicked -= OFormActMain_UpdateCheckClicked;

            _pluginStatusText.Text = "Plugin stopped";
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _settings = new WhatRuneSettings();

            _pluginScreenSpace = pluginScreenSpace;
            _pluginStatusText = pluginStatusText;
            InitUI();

            Log("Initializing plugin");
            Log("Version = " + GetType().Assembly.GetName().Version);
            ShowLog();

            // Kick off a timer to do additional initialization
            _timerFetchRunes.Interval = DELAY_INIT_SECONDS;
            if (DEBUG)
            {
                _timerFetchRunes.Interval = 100;
            }
            _timerFetchRunes.Start();

            try
            {
                // Update pattern for file download
                // See: https://gist.github.com/EQAditu/4d6e3a1945fed2199f235fedc1e3ec56#Act_Plugin_Update.cs
                ActGlobals.oFormActMain.UpdateCheckClicked += OFormActMain_UpdateCheckClicked;
                if (ActGlobals.oFormActMain.GetAutomaticUpdatesAllowed())
                {
                    new System.Threading.Thread(new System.Threading.ThreadStart(OFormActMain_UpdateCheckClicked)) { IsBackground = true }.Start();
                }
            }
            catch (Exception ex)
            {
                Log("Error updating: " + ex.Message);
            }

            ActGlobals.oFormActMain.OnLogLineRead += oFormActMain_OnLogLineRead;
        }

        private void InitUI()
        {
            _pluginStatusText.Text = "Plugin started";
            _pluginScreenSpace.Text = "WhatRune";

            Panel pnlControls = new Panel();
            pnlControls.Dock = DockStyle.Top;
            int y = 5;

            Label lblMacro = new Label()
            {
                Text = "Macro contents (rune info will replace \"$R\"):",
                AutoSize = true,
                Top = y
            };
            pnlControls.Controls.Add(lblMacro);
            y = lblMacro.Bottom;

            _textBoxMacro = new TextBox()
            {
                ScrollBars = ScrollBars.Both,
                Multiline = true,
                Height = 75,
                Width = 400,
                Top = y,
                Left = 3,
                Text = _settings.MacroCommands
            };
            _textBoxMacro.TextChanged -= _textBoxMacro_TextChanged;
            _textBoxMacro.TextChanged += _textBoxMacro_TextChanged;
            pnlControls.Controls.Add(_textBoxMacro);
            y = _textBoxMacro.Bottom + 5;

            _btnFetchDefs = new Button()
            {
                Text = "Refresh Definitions",
                AutoSize = true,
                Top = y
            };
            _btnFetchDefs.Click -= _btnFetchDefs_Click;
            _btnFetchDefs.Click += _btnFetchDefs_Click;
            pnlControls.Controls.Add(_btnFetchDefs);

            Button btnShowHelp = new Button()
            {
                Text = "Show Help",
                AutoSize = true,
                Top = y,
                Left = _btnFetchDefs.Right + 5
            };
            btnShowHelp.Click -= _btnFetchDefs_Click;
            btnShowHelp.Click += BtnShowHelp_Click;
            pnlControls.Controls.Add(btnShowHelp);
            y = _btnFetchDefs.Bottom + 5;

            pnlControls.Height = y;

            _textBoxLogs = new TextBox()
            {
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                Multiline = true,
                ReadOnly = true
            };
            _pluginScreenSpace.Text = "WhatRune";

            _pluginScreenSpace.Controls.Add(_textBoxLogs);
            _pluginScreenSpace.Controls.Add(pnlControls);
        }

        private void BtnShowHelp_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(HELP_PAGE);
        }

        private void _textBoxMacro_TextChanged(object sender, EventArgs e)
        {
            _settings.MacroCommands = _textBoxMacro.Text;
            _settings.SaveSettings();
        }

        public void Log(string message)
        {
            // Need to lock the collection since both the parsing and UI threads can write to it
            lock (Logs)
            {
                Logs.Add(String.Format("[{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), message));
            }
        }

        public void ShowLog()
        {
            // Update the UI with a thread-safe invocation
            if (_pluginScreenSpace.InvokeRequired)
            {
                _pluginScreenSpace.Invoke(new Action(() => ShowLog()));
            }
            else
            {
                lock (Logs)
                {
                    _textBoxLogs.Lines = Logs.ToArray();
                }
                _textBoxLogs.SelectionStart = _textBoxLogs.Text.Length;
                _textBoxLogs.ScrollToCaret();
            }
        }

        private async Task FetchRuneDefs()
        {
            Log("Fetching rune definitions...");
            Runes.Clear();

            try
            {
                string runeDefs = "";
                if (DEBUG)
                {
                    runeDefs = File.ReadAllText(@"C:\Dev\ActWhatRune\ActWhatRune\runes.txt");
                }
                else
                {
                    using (HttpClient client = new HttpClient())
                    {
                        runeDefs = await client.GetStringAsync(RUNES_DEFS);
                    }
                }
                Log("Got rune data");

                foreach (string line in runeDefs.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.StartsWith("#"))
                    {
                        string[] defParts = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                        if (defParts.Length > 0)
                        {
                            // The part of the string on the left of "=" can be a comma separated list of aliases,
                            // or in multi-name encounters the complete set of mob names
                            string[] mobNames = defParts[0].Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);

                            string rune = "No specific rune";
                            if (defParts.Length >= 2)
                            {
                                // Let's make this part future-proof. We'll separate info bits using a token: ";"
                                string info = defParts[1];
                                string[] infoParts = info.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                                // The first part is what rune to wear
                                if (infoParts.Length >= 1)
                                {
                                    rune = infoParts[0];
                                }
                            }

                            // If we get duplicate mobs, only add the first one
                            foreach (string mobName in mobNames)
                            {
                                string key = mobName.ToLower();
                                if (!Runes.ContainsKey(key))
                                {
                                    Runes.TryAdd(key, rune);
                                }
                            }
                        }
                    }
                }
                Log($"Found {Runes.Count} definition(s)");
            }
            catch (Exception ex)
            {
                Log("Unable to get rune data: " + ex.Message);
            }

            ShowLog();
        }

        private void _btnFetchDefs_Click(object sender, EventArgs e)
        {
            _timerFetchRunes.Start();
        }

        private async void _timerFetchRunes_Tick(object sender, EventArgs e)
        {
            // Kill the timer, this is a singular event
            _timerFetchRunes.Stop();

            await FetchRuneDefs();
        }

        private void OFormActMain_UpdateCheckClicked()
        {
            if (PLUGIN_ID < int.MaxValue)
            {
                // This ID must be the same ID used on ACT's website.
                int pluginId = PLUGIN_ID;
                string pluginName = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title;
                try
                {
                    Version localVersion = GetType().Assembly.GetName().Version;
                    Version remoteVersion = new Version(ActGlobals.oFormActMain.PluginGetRemoteVersion(pluginId).TrimStart(new char[] { 'v' }));
                    if (remoteVersion > localVersion)
                    {
                        DialogResult result = MessageBox.Show(
                            $"There is an updated version of the {pluginName} plugin.  Update it now?\n\n(If there is an update to ACT, you should click No and update ACT first.)",
                            "New Version", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (result == DialogResult.Yes)
                        {
                            FileInfo updatedFile = ActGlobals.oFormActMain.PluginDownload(pluginId);
                            ActPluginData pluginData = ActGlobals.oFormActMain.PluginGetSelfData(this);
                            pluginData.pluginFile.Delete();
                            updatedFile.MoveTo(pluginData.pluginFile.FullName);

                            // You can choose to simply restart the plugin, if the plugin can properly clean-up in DeInit
                            // and has no external assemblies that update.
                            ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, false);
                            Application.DoEvents();
                            ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, $"Plugin Update Check - {pluginName}");
                }
            }
        }

        // This is called each time ACT detects a new log line
        private void oFormActMain_OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            // Scan for consider messages, eg:
            //   \#FFFF40You consider High Shikari Olyxa... It looks tough -and it's a LOT tougher than it looks.  Better have a lot of backup for this one!
            // or, /rune MobName
            //   Unknown command: 'rune Mayong'
            try
            {
                string mobName = null;

                string logLine = logInfo.logLine;
                int startPos = logLine.IndexOf("]");
                if (startPos >= 0)
                {
                    logLine = logLine.Substring(startPos + 2);

                    if (REGEX_CONSIDER.IsMatch(logLine))
                    {
                        startPos = logLine.IndexOf(CONSIDER_TOKEN) + CONSIDER_TOKEN.Length + 1;
                        int endPos = logLine.IndexOf("...", startPos);
                        if (endPos >= 0)
                        {
                            mobName = logLine.Substring(startPos, (endPos - startPos));
                        }
                    }
                    else if (REGEX_RUNE.IsMatch(logLine))
                    {
                        startPos = logLine.IndexOf(RUNE_TOKEN) + RUNE_TOKEN.Length;
                        int endPos = logLine.LastIndexOf("'");
                        if (endPos >= 0)
                        {
                            mobName = logLine.Substring(startPos, (endPos - startPos));
                        }
                    }
                }

                if (!string.IsNullOrEmpty(mobName))
                {
                    Log("Considering " + mobName);
                    string runeInfo = "Unknown rune";

                    string key = mobName.ToLower();
                    if (Runes.ContainsKey(key))
                    {
                        runeInfo = Runes[key];
                    }
                    Log("  " + runeInfo);
                    ActGlobals.oFormActMain.TTS(runeInfo);
                    WriteMacroFile(runeInfo);
                    ShowLog();
                }
            }
            catch { } // Black hole...
        }

        protected void WriteMacroFile(string runeInfo)
        {
            StringBuilder fileLines = new StringBuilder();
            foreach (string line in _settings.MacroCommands.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                string fileLine = line.Trim();
                if (!string.IsNullOrEmpty(fileLine))
                {
                    if (line.StartsWith("/"))
                    {
                        fileLine = line.Substring(1);
                    }

                    fileLine = fileLine.Replace("$R", runeInfo);
                    fileLines.AppendLine(fileLine);
                }
            }

            try
            {
                ActGlobals.oFormActMain.SendToMacroFile(MACRO_FILENAME, fileLines.ToString(), "");
                Log("Wrote to macro file: " + Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, MACRO_FILENAME));
            }
            catch (Exception ex)
            {
                Log("Failed to write macro file: " + Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, MACRO_FILENAME));
                Log(ex.Message);
            }
        }
    }
}
