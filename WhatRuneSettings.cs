using System;
using System.IO;
using System.Text;
using System.Xml;
using Advanced_Combat_Tracker;

namespace ACT_Plugin
{
    class WhatRuneSettings
    {
        private string _settingsFile = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\WhatRune.config.xml");
        private SettingsSerializer _xmlSettings;

        public string MacroCommands = "";

        public WhatRuneSettings()
        {
            _xmlSettings = new SettingsSerializer(this);
            _xmlSettings.AddStringSetting("MacroCommands");

            LoadSettings();
        }

        public void LoadSettings()
        {
            if (File.Exists(_settingsFile))
            {
                var fs = new FileStream(_settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var xReader = new XmlTextReader(fs);

                try
                {
                    while (xReader.Read())
                    {
                        if (xReader.NodeType == XmlNodeType.Element)
                        {
                            if (xReader.LocalName == "SettingsSerializer")
                            {
                                _xmlSettings.ImportFromXml(xReader);
                            }
                        }
                    }
                }
                catch { }
                xReader.Close();
            }
        }

        public void SaveSettings()
        {
            try
            {
                FileStream fs = new FileStream(_settingsFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                XmlTextWriter xWriter = new XmlTextWriter(fs, Encoding.UTF8);
                xWriter.Formatting = Formatting.Indented;
                xWriter.Indentation = 1;
                xWriter.IndentChar = '\t';
                xWriter.WriteStartDocument(true);
                xWriter.WriteStartElement("Config");
                xWriter.WriteStartElement("SettingsSerializer");
                _xmlSettings.ExportToXml(xWriter);
                xWriter.WriteEndElement();
                xWriter.WriteEndElement();
                xWriter.WriteEndDocument();
                xWriter.Flush();
                xWriter.Close();
            }
            catch { }
        }
    }
}
