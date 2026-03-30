using System;
using System.IO;

namespace ValheimVoiceServer
{
    public class ServerConfig
    {
        public int Port { get; set; } = 9876;
        public float MaxVoiceDistance { get; set; } = 50f;
        public float FadeStartDistance { get; set; } = 5f;
        public int ClientTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Load config from command line args and/or config file.
        /// Args: --port 9876 --distance 50 --fade 5 --timeout 30
        /// Also reads voiceserver.cfg if present in the working directory.
        /// </summary>
        public static ServerConfig Load(string[] args)
        {
            var config = new ServerConfig();

            // Load from file first
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voiceserver.cfg");
            if (File.Exists(configPath))
            {
                foreach (string line in File.ReadAllLines(configPath))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = trimmed.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "port":
                            if (int.TryParse(value, out int p)) config.Port = p;
                            break;
                        case "maxvoicedistance":
                            if (float.TryParse(value, out float d)) config.MaxVoiceDistance = d;
                            break;
                        case "fadestartdistance":
                            if (float.TryParse(value, out float f)) config.FadeStartDistance = f;
                            break;
                        case "clienttimeout":
                            if (int.TryParse(value, out int t)) config.ClientTimeoutSeconds = t;
                            break;
                    }
                }
                Console.WriteLine($"Loaded config from {configPath}");
            }

            // Override with command line args
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--port":
                    case "-p":
                        if (int.TryParse(args[i + 1], out int port)) config.Port = port;
                        i++;
                        break;
                    case "--distance":
                    case "-d":
                        if (float.TryParse(args[i + 1], out float dist)) config.MaxVoiceDistance = dist;
                        i++;
                        break;
                    case "--fade":
                        if (float.TryParse(args[i + 1], out float fade)) config.FadeStartDistance = fade;
                        i++;
                        break;
                    case "--timeout":
                        if (int.TryParse(args[i + 1], out int timeout)) config.ClientTimeoutSeconds = timeout;
                        i++;
                        break;
                }
            }

            return config;
        }
    }
}
