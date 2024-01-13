using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Metrist.Runner
{
    /**
     * <summary>
     * Implementation of the protocol between monitoring scheduler and monitoring executables. For
     * a protocol description, see the orchestrator documentation.
     * </summary>
     */
    public class Protocol
    {
        public static int Major = 1;
        public static int Minor = 1;

        private TextReader _in;
        private TextWriter _out;

        private int _major;
        private int _minor;
        private Dictionary<string, object> _config;

        public Protocol(System.IO.TextReader @in, System.IO.TextWriter @out)
        {
            _in = @in;
            _out = @out;
            Handshake();
        }

        // Order is that METRIST_ and CANARY_ environment variables override jsonConfig.
        public object GetConfigValue(string monitorLogicalName, string propertyName)
        {
            var envName = SnakeAndUpcase(monitorLogicalName) + SnakeAndUpcase(propertyName);
            LogDebug("- try env " + envName);

            var envVal = System.Environment.GetEnvironmentVariable("METRIST_" + envName);
            if (envVal != null)
            {
                LogDebug("- set/override value from from Metrist env var");
                return envVal;
            }

            envVal = System.Environment.GetEnvironmentVariable("CANARY_" + envName);
            if (envVal != null)
            {
                LogDebug("- OBSOLOTE set/override value from from Canary env var");
                return envVal;
            }

            if (_config.ContainsKey(propertyName))
            {
                LogDebug("- set value from from runner config");
                return _config[propertyName];
            }

            return null;
        }

        public String GetStep(Action<bool> cleanupHandler)
        {
            var msg = Read();
            if (msg == "Exit 0")
            {
                cleanupHandler(false);
                return null;
            }
            if (msg == "Exit 1")
            {
                cleanupHandler(true);
                return null;
            }
            if (msg.StartsWith("Run Step "))
            {
                var parts = msg.Split(" ");
                return parts[2];
            }
            else
            {
                LogError($"Unexpected message \"{msg}\", assuming exit");
                return null;
            }
        }

        public void Logger(string msg)
        {
            LogInfo(msg);
        }

        public void SendTime(double time)
        {
            Write("Step Time " + time);
        }

        public void SendOK()
        {
            Write("Step OK");
        }

        public void SendError(string error)
        {
            Write("Step Error " + error);
        }

        public void LogDebug(string msg)
        {
            Write("Log Debug " + msg);
        }

        public void LogInfo(string msg)
        {
            Write("Log Info " + msg);
        }

        public void LogWarning(string msg)
        {
            Write("Log Warning " + msg);
        }

        public void LogError(string msg)
        {
            Write("Log Error " + msg);
        }

        public void Configured()
        {
            Write("Configured");
        }

        public void Exit()
        {
            Write("Exit");
        }

        public string WaitForWebhook(string uid)
        {
            Write($"Wait For Webhook {uid}");
            var groups = Expect("Webhook Wait Response (.*)");
            return groups[1].ToString();
        }

        private void Handshake()
        {
            Write($"Started {Major}.{Minor}");
            var groups = Expect("Version ([0-9]+)\\.([0-9]+)");
            // groups[0] is the whole matched string, groups 1 and 2 our explicit grouping.
            // does not seem to be entirely in line with the docs but that's how it works.
            _major = Int32.Parse(groups[1].ToString());
            _minor = Int32.Parse(groups[2].ToString());
            AssertCompatibility();
            Write("Ready");
            groups = Expect("Config (.*)");
            _config = JsonConvert.DeserializeObject<Dictionary<string,object>>(groups[1].ToString());
        }

        private void AssertCompatibility()
        {
            // For now, KISS
            var isCompatible = (_major == Major) && (_minor >= Minor);
            if (!isCompatible)
            {
                throw new Exception($"Protocol incompatible. We support {Major}.{Minor}, orchestration supports {_major}.{_minor}");
            }
        }

        private GroupCollection Expect(string re)
        {
            var msg = Read();
            var match = new Regex(re).Match(msg);
            if (match.Length == 0)
            {
                throw new Exception($"Unexpected message; wanted=\"{re}\", received=\"msg\"");
            }
            return match.Groups;
        }

        private string Read()
        {
            var buff = new char[6];
            var read = _in.ReadBlock(buff, 0, 6);
            if (read != 6)
            {
                throw new Exception($"Could not read message length, got {read} instead");
            }
            var length = Int32.Parse(buff);
            if (length <= 0)
            {
                throw new Exception($"Unexpected length: {length} read");
            }
            buff = new char[length];
            read = _in.ReadBlock(buff, 0, length);
            if (read != length)
            {
                throw new Exception($"Could not read message to full length of {length}, got {read} instead");
            }
            return new string(buff);
        }

        private void Write(string v)
        {
            _out.Flush(); // Flush any contents that may still be in the buffer
            _out.Write("{0} {1}", Encoding.UTF8.GetBytes(v).Length, v);
            _out.Flush();
        }

        // Convert PascalCase or camelCase to uppercased SNAKE_CASE. Note that PascalCase will return
        // with a leading underscore
        private string SnakeAndUpcase(string camelized)
        {
            return Regex.Replace(camelized, "[A-Z]", "_$0").ToUpper();
        }
    }
}
