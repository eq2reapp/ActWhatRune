using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

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

        public static int PLUGIN_ID = int.MaxValue;
        public static Regex REGEX_CONSIDER = new Regex(@"\\#[ABCDEF0-9]{6}You consider");
        public static string RUNES_DEFS = "https://raw.githubusercontent.com/eq2reapp/ActWhatRune/main/runes.txt";

        private TabPage _pluginScreenSpace = null;
        private Label _pluginStatusText = null;
        private Panel _pnlControls = null;
        private Button _btnFetchDefs = null;
        private TextBox _textBoxLogs = null;
        private List<String> Logs = new List<string>();
        private ConcurrentDictionary<string, string> Runes = new ConcurrentDictionary<string, string>();
        private Timer _timerFetchRunes = new Timer();
        private const int DELAY_ATTACH_SECONDS = 2 * 1000;

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
            _pluginScreenSpace = pluginScreenSpace;
            _pluginStatusText = pluginStatusText;
            InitUI();

            Log("Initializing plugin");
            Log("Version = " + GetType().Assembly.GetName().Version);
            ShowLog();

            // Kick off a timer to do additional initialization
            _timerFetchRunes.Interval = DELAY_ATTACH_SECONDS;
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

            _pnlControls = new Panel();
            _pnlControls.Dock = DockStyle.Top;

            _btnFetchDefs = new Button();
            _btnFetchDefs.Click -= _btnFetchDefs_Click;
            _btnFetchDefs.Click += _btnFetchDefs_Click;
            _btnFetchDefs.Text = "Refresh Definitions";
            _btnFetchDefs.AutoSize = true;
            _pnlControls.Controls.Add(_btnFetchDefs);
            _pnlControls.Height = _btnFetchDefs.Height + 4;

            _textBoxLogs = new TextBox()
            {
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                Multiline = true,
                ReadOnly = true
            };
            _pluginScreenSpace.Text = "WhatRune";

            _pluginScreenSpace.Controls.Add(_textBoxLogs);
            _pluginScreenSpace.Controls.Add(_pnlControls);
        }

        public void Log(string message)
        {
            lock (Logs)
            {
                Logs.Add(String.Format("[{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), message));
            }
        }

        public void ShowLog()
        {
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
                using (HttpClient client = new HttpClient())
                {
                    runeDefs = await client.GetStringAsync(RUNES_DEFS);
                }
                Log("Got rune data");

                foreach (string line in runeDefs.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] defParts = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (defParts.Length == 2)
                    {
                        string mobName = defParts[0].Trim();
                        string rune = defParts[1].Trim();
                        if (!Runes.ContainsKey(mobName))
                        {
                            Runes.TryAdd(mobName, rune);
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
            try
            {
                string logLine = logInfo.logLine;
                int startPos = logLine.IndexOf("]");
                if (startPos >= 0)
                {
                    logLine = logLine.Substring(startPos + 2);
                    if (REGEX_CONSIDER.IsMatch(logLine))
                    {
                        startPos = 21;
                        int endPos = logLine.IndexOf("...", startPos);
                        if (endPos >= 0)
                        {
                            string mobName = logLine.Substring(startPos, (endPos - startPos));
                            Log("Considering " + mobName);
                            if (Runes.ContainsKey(mobName))
                            {
                                Log("  " + Runes[mobName]);
                                ActGlobals.oFormActMain.TTS(Runes[mobName]);
                            }
                            else
                            {
                                Log("  Unknown...");
                                ActGlobals.oFormActMain.TTS("Unknown rune");
                            }
                            ShowLog();
                        }
                    }
                }
            }
            catch { } // Black hole...
        }
    }
}
