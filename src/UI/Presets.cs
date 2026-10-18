using System.Collections.Generic;

namespace WinTools.UI
{
    /// <summary>Strumenti di Windows pronti da aggiungere alla barra (come i collegamenti rapidi di WinOptima) e glifi per i tasti.</summary>
    public static class Presets
    {
        public sealed class Tool
        {
            public string Group, Label, Target, Arguments, Glyph;
            public bool Admin;
        }

        public static readonly List<Tool> Tools = new List<Tool>
        {
            // Cartelle
            new Tool { Group = "Cartelle", Label = "Esplora file", Target = "explorer.exe", Glyph = "EC50" },
            new Tool { Group = "Cartelle", Label = "Questo PC", Target = "shell:MyComputerFolder", Glyph = "E7F4" },
            new Tool { Group = "Cartelle", Label = "Desktop", Target = "shell:Desktop", Glyph = "E7F8" },
            new Tool { Group = "Cartelle", Label = "Documenti", Target = "shell:Personal", Glyph = "E8A5" },
            new Tool { Group = "Cartelle", Label = "Download", Target = "shell:Downloads", Glyph = "E896" },
            new Tool { Group = "Cartelle", Label = "Immagini", Target = "shell:My Pictures", Glyph = "EB9F" },
            new Tool { Group = "Cartelle", Label = "Cestino", Target = "shell:RecycleBinFolder", Glyph = "E74D" },
            new Tool { Group = "Cartelle", Label = "Cartella utente", Target = "%USERPROFILE%", Glyph = "E77B" },
            new Tool { Group = "Cartelle", Label = "Esecuzione automatica", Target = "shell:Startup", Glyph = "E7E8" },
            // Impostazioni
            new Tool { Group = "Impostazioni", Label = "Impostazioni", Target = "ms-settings:", Glyph = "E713" },
            new Tool { Group = "Impostazioni", Label = "Windows Update", Target = "ms-settings:windowsupdate", Glyph = "E895" },
            new Tool { Group = "Impostazioni", Label = "Rete e Internet", Target = "ms-settings:network", Glyph = "E774" },
            new Tool { Group = "Impostazioni", Label = "Wi-Fi", Target = "ms-settings:network-wifi", Glyph = "E701" },
            new Tool { Group = "Impostazioni", Label = "Bluetooth e dispositivi", Target = "ms-settings:bluetooth", Glyph = "E702" },
            new Tool { Group = "Impostazioni", Label = "Schermo", Target = "ms-settings:display", Glyph = "E7F4" },
            new Tool { Group = "Impostazioni", Label = "Audio", Target = "ms-settings:sound", Glyph = "E767" },
            new Tool { Group = "Impostazioni", Label = "App installate", Target = "ms-settings:appsfeatures", Glyph = "E71D" },
            new Tool { Group = "Impostazioni", Label = "Personalizzazione", Target = "ms-settings:personalization", Glyph = "E771" },
            new Tool { Group = "Impostazioni", Label = "Stampanti", Target = "ms-settings:printers", Glyph = "E749" },
            new Tool { Group = "Impostazioni", Label = "Pannello di controllo", Target = "control.exe", Glyph = "E90F" },
            new Tool { Group = "Impostazioni", Label = "Programmi e funzionalità", Target = "appwiz.cpl", Glyph = "E71D" },
            new Tool { Group = "Impostazioni", Label = "Connessioni di rete", Target = "ncpa.cpl", Glyph = "E839" },
            new Tool { Group = "Impostazioni", Label = "Risparmio energia", Target = "powercfg.cpl", Glyph = "E83E" },
            // Gestione del sistema
            new Tool { Group = "Sistema", Label = "Gestione attività", Target = "taskmgr.exe", Glyph = "E9D9" },
            new Tool { Group = "Sistema", Label = "Gestione computer", Target = "compmgmt.msc", Glyph = "E770" },
            new Tool { Group = "Sistema", Label = "Gestione dispositivi", Target = "devmgmt.msc", Glyph = "E772" },
            new Tool { Group = "Sistema", Label = "Gestione disco", Target = "diskmgmt.msc", Glyph = "EDA2" },
            new Tool { Group = "Sistema", Label = "Servizi", Target = "services.msc", Glyph = "E912" },
            new Tool { Group = "Sistema", Label = "Utilità di pianificazione", Target = "taskschd.msc", Glyph = "E787" },
            new Tool { Group = "Sistema", Label = "Visualizzatore eventi", Target = "eventvwr.msc", Glyph = "E7BA" },
            new Tool { Group = "Sistema", Label = "Monitoraggio risorse", Target = "resmon.exe", Glyph = "E9F5" },
            new Tool { Group = "Sistema", Label = "Informazioni di sistema", Target = "msinfo32.exe", Glyph = "E946" },
            new Tool { Group = "Sistema", Label = "Editor del Registro", Target = "regedit.exe", Glyph = "E8F1" },
            new Tool { Group = "Sistema", Label = "Windows Defender Firewall", Target = "wf.msc", Glyph = "E72E" },
            new Tool { Group = "Sistema", Label = "Pulizia disco", Target = "cleanmgr.exe", Glyph = "EA99" },
            new Tool { Group = "Sistema", Label = "Proprietà del sistema", Target = "sysdm.cpl", Glyph = "E7F4" },
            // Terminali
            new Tool { Group = "Terminali", Label = "Terminale", Target = "wt.exe", Glyph = "E756" },
            new Tool { Group = "Terminali", Label = "Prompt dei comandi", Target = "cmd.exe", Glyph = "E756" },
            new Tool { Group = "Terminali", Label = "Prompt dei comandi (amministratore)", Target = "cmd.exe", Glyph = "E756", Admin = true },
            new Tool { Group = "Terminali", Label = "PowerShell", Target = "powershell.exe", Glyph = "E756" },
            new Tool { Group = "Terminali", Label = "PowerShell (amministratore)", Target = "powershell.exe", Glyph = "E756", Admin = true },
            // Accessori
            new Tool { Group = "Accessori", Label = "Calcolatrice", Target = "calc.exe", Glyph = "E8EF" },
            new Tool { Group = "Accessori", Label = "Blocco note", Target = "notepad.exe", Glyph = "E70B" },
            new Tool { Group = "Accessori", Label = "Paint", Target = "mspaint.exe", Glyph = "E790" },
            new Tool { Group = "Accessori", Label = "Cattura schermo", Target = "ms-screenclip:", Glyph = "E722" },
            new Tool { Group = "Accessori", Label = "Mappa caratteri", Target = "charmap.exe", Glyph = "E8D2" },
            new Tool { Group = "Accessori", Label = "Lente di ingrandimento", Target = "magnify.exe", Glyph = "E71E" },
            new Tool { Group = "Accessori", Label = "Tastiera su schermo", Target = "osk.exe", Glyph = "E765" },
            new Tool { Group = "Accessori", Label = "Registratore passaggi", Target = "psr.exe", Glyph = "E714" },
            new Tool { Group = "Accessori", Label = "Connessione Desktop remoto", Target = "mstsc.exe", Glyph = "E8AF" },
            new Tool { Group = "Accessori", Label = "Blocca il PC", Target = "rundll32.exe", Arguments = "user32.dll,LockWorkStation", Glyph = "E72E" },
            // Web
            new Tool { Group = "Web", Label = "GitHub", Target = "https://github.com", Glyph = "E774" },
            new Tool { Group = "Web", Label = "Google", Target = "https://www.google.com", Glyph = "E721" },
            new Tool { Group = "Web", Label = "Gmail", Target = "https://mail.google.com", Glyph = "E715" },
            new Tool { Group = "Web", Label = "YouTube", Target = "https://www.youtube.com", Glyph = "E714" },
            new Tool { Group = "Web", Label = "Maps", Target = "https://www.google.com/maps", Glyph = "E707" },
        };

        /// <summary>Glifi di Segoe Fluent Icons tra cui scegliere per un tasto (codice, nome).</summary>
        public static readonly string[] Glyphs =
        {
            "E8B7", "EC50", "E838", "E8A5", "E896", "EB9F", "E74D", "E77B", "E7F4", "E7F8",
            "E713", "E895", "E774", "E701", "E702", "E767", "E71D", "E771", "E749", "E90F",
            "E9D9", "E770", "E772", "EDA2", "E912", "E787", "E7BA", "E9F5", "E946", "E8F1",
            "E72E", "EA99", "E756", "E8EF", "E70B", "E790", "E722", "E8D2", "E71E", "E765",
            "E714", "E8AF", "E715", "E717", "E716", "E721", "E707", "E753", "E7C3", "E8A1",
            "E945", "E734", "E718", "E8C8", "E74E", "E768", "E8D6", "E7E8", "E72C", "E710",
            "E739", "E73E", "E711", "E7BE", "E8FD", "E8EC", "E8B9", "E80F", "E823", "E70F",
            "E82D", "E8F3", "E9CE", "E9F3", "EA37", "EB51", "EB9D", "EC4E", "ECAD", "F0E2"
        };
    }
}
