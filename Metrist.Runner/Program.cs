using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Net.Http;
using System.Runtime.Loader;

namespace Metrist.Runner
{
    /**
     * <summary>
     * This class contains the code to invoke monitors that are distributed as DLL
     * files. It will start the DLL in the first argument and instantiate a monitor
     * from it. It will eventually also implement the orchestrator<->monitor protocol.
     * </summary>
     */
    public class Program
    {

        private const string BASE_MONITOR_CONFIG_TYPE_NAME = "Metrist.Core.BaseMonitorConfig";
        private const string LOGGER_TYPE_NAME = "Metrist.Core.Logger";
        private const string WAIT_FOR_WEBHOOK_TYPE_NAME = "Metrist.Core.WaitForWebhook";

        // All details that get passed to the monitor have to be loaded via reflection
        // using the monitors version of the Shared.Core.dll. Using a local Shard.Core.Dll can result in
        // cast/type problems as the 2 versions could be different
        // _monitor, _config, and _loggingDelegate are impacted
        private object _monitor;
        private object _config;
        private Type _configType;
        private object _loggingDelegate;
        private object _webhookWaitDelegate;
        private Type _webhookWaitType;
        private string _instanceIdentifier;
        private Protocol _proto;
        private Type _monitorType;
        private ILoggerFactory _loggerFactory;
        private ILogger _logger;
        private HttpClient _httpClient;
        private bool _disableCache;
        private bool _previewMode;
        private const string MONITOR_DISTRIBUTIONS_URL = "https://monitor-distributions.metrist.io";

        public static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Needs two arguments: <name_of_monitor> <local_path_to_monitor>");
                return 1;
            }

            var p = new Program();
            try
            {
                return p.Run(args[0], args[1]);
            }
            catch (Exception e)
            {
                var msg = $"Unhandled exception, exiting: {e.ToString()}";
                if (p._proto != null)
                {
                    p._proto.LogError(msg);
                }
                else
                {
                    Console.WriteLine(msg);
                }
                return 1;
            }
        }


        public int Run(string name, string executableFolder)
        {
            InitializeLogging();
            _instanceIdentifier = Environment.GetEnvironmentVariable("METRIST_INSTANCE_ID") ?? System.Net.Dns.GetHostName();
            _httpClient = new HttpClient();

            // Get the stdout TextWriter, then set Console's ouput to a StringWriter that will be
            // logged separately after each step in order to prevent random Console writes from interfering
            TextWriter stdout = Console.Out;
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);

            _proto = new Protocol(Console.In, stdout);
            _disableCache = Environment.GetEnvironmentVariable("METRIST_DLL_RUNNER_DISABLE_CACHING") != null;
            _previewMode = Environment.GetEnvironmentVariable("METRIST_PREVIEW_MODE") != null;
            string dllName = null;

            _proto.LogInfo($"Running using executable folder {executableFolder}");
            dllName = FindTopLevelDll(executableFolder);

            var packageName = dllName.Split("/").Last().Replace(".dll", "");
            var monitorName = packageName + ".Monitor";
            var configName = packageName + ".MonitorConfig";
            var coreDllName = Path.Join(Path.GetDirectoryName(dllName),"Metrist.Core.dll");

            _proto.LogInfo($"Expecting monitor class {monitorName} and config class {configName}");

            Assembly dll = Assembly.LoadFrom(dllName);
            _proto.LogInfo("Dll: " + dll.ToString());

            Assembly coreDll = Assembly.LoadFrom(coreDllName);
            _proto.LogInfo("Core Dll: " + dll.ToString());

            var loggerType = coreDll.GetType(LOGGER_TYPE_NAME);
            _loggingDelegate = Delegate.CreateDelegate(loggerType, _proto, _proto.GetType().GetMethod("Logger", BindingFlags.Public | BindingFlags.Instance));

            if (!MaybeCreateWaitForWebhookDelegate(coreDll))
            {
                return 1;
            }

            if (!LoadConfig(name, configName, coreDll, dll))
            {
                return 1;
            }

            _proto.LogInfo("==== Instantiating monitor");

            if (!InstantiateMonitor(dll, monitorName))
            {
                return 1;
            }

            _proto.LogDebug($"Monitor type is {_monitorType}");

            _proto.Configured();
            _proto.LogInfo("Configured, ready for stepping");

            RunSteps(sw);

            _proto.LogInfo("All done!");
            _proto.Exit();

            return 0;
        }

        private bool LoadConfig(string name, string configName, Assembly coreDll, Assembly dll)
        {
            var baseConfigType = coreDll.GetType(BASE_MONITOR_CONFIG_TYPE_NAME);
            var baseConfigCtor = baseConfigType.GetConstructor(new Type[] { });
            if (baseConfigCtor == null)
            {
                _proto.LogInfo($"Could not find a default constructor for {BASE_MONITOR_CONFIG_TYPE_NAME}");
                return false;
            }

            // Base monitor config should always have these properties
            var baseConfig = baseConfigCtor.Invoke(new object[] {});
            try
            {
                SetProperty(baseConfigType, baseConfig, "MonitorId", name);
                SetProperty(baseConfigType, baseConfig, "InstanceIdentifier", _instanceIdentifier);
                SetProperty(baseConfigType, baseConfig, "IsCleanupEnabled", true);
            }
            catch (ArgumentException ex)
            {
                _proto.LogInfo(ex.Message);
                return false;
            }

            var logger = _loggerFactory.CreateLogger(name);

            // There are two possibilities: the unlikely possibility that the config class is empty,
            // or the more likely possibility that the config class takes a base config in its ctor.

            _configType = dll.GetType(configName);
            var configCtor = _configType.GetConstructor(new Type[] { });
            if (configCtor != null)
            {
                _config = configCtor.Invoke(new object[] { });
            }
            else
            {
                configCtor = _configType.GetConstructor(new Type[] { baseConfig.GetType() });
                if (configCtor == null)
                {
                    _proto.LogInfo($"Could not find a default or one-arg (with BaseMonitorConfig) constructor for {configName}");
                    return false;
                }
                _config = configCtor.Invoke(new object[] { baseConfig });
            }
            if (_config == null)
            {
                _proto.LogInfo($"Could not create instance of config class");
                return false;
            }

            SetConfigValues(name, baseConfigType);

            return true;
        }

        private bool InstantiateMonitor(Assembly monitorDll, string monitorName)
        {
            _monitorType = monitorDll.GetType(monitorName);
            var ctor = _monitorType.GetConstructor(new Type[] { _configType });
            if (ctor == null)
            {
                _proto.LogError($"Could not find standard constructor");
                return false;
            }

            try
            {
                _monitor = ctor.Invoke(new object[] { _config });
                if (_monitor == null)
                {
                    _proto.LogError($"Could not create instance of monitor");
                    return false;
                }
            }
            catch (Exception e)
            {
                _proto.LogError($"Exception in monitor constructor: {e}");
                return false;
            }

            return true;
        }

        private void RunSteps(StringWriter sw)
        {
            string nextStep;
            while ((nextStep = _proto.GetStep(OnExit)) != null)
            {
                var stepFun = _monitorType.GetMethod(nextStep, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (stepFun == null)
                {
                    _proto.SendError($"Function for {nextStep} is missing!");
                }
                else
                {
                    _proto.LogInfo($"Invoking step {nextStep}");
                    try
                    {
                        ExecuteStep(_monitor, stepFun);

                        // Send Console.Out writes here as debug log and clear StringWriter's contents
                        var consoleOutWrites = sw.ToString();
                        if (consoleOutWrites.Trim().Length > 0)
                        {
                            // Since the protocol on the orchestrator side trims, writing 0 length strings can be dangerous as you can end up with LOG DEBUG<number>
                            // which won't parse causing the proto to exit on the orchestrator side resulting in
                            // "22:21:18.698 [error] Unexpected message: ["Log Debug0"] received, exiting" and the other steps not being run
                            _proto.LogDebug(consoleOutWrites);
                        }
                        sw.GetStringBuilder().Clear();
                    }
                    catch (Exception e)
                    {
                        _proto.SendError($"Exception in step {nextStep}: {e.ToString()}");
                    }
                }
            }
        }

        private void SetConfigValues(string name, Type baseConfigType)
        {
            foreach (var f in _configType.GetProperties())
            {
                if (f.DeclaringType == baseConfigType)
                {
                    _proto.LogDebug($"Skip protected property #{f}");
                }
                else if (f.PropertyType == _webhookWaitType && _webhookWaitDelegate != null)
                {
                    _proto.LogInfo($"Setting webhook wait delegate for property #{f}");
                    f.GetSetMethod().Invoke(_config, new object[] { _webhookWaitDelegate });
                }
                else
                {
                    _proto.LogInfo($"Configuring monitor property #{f}");
                    var configValue = _proto.GetConfigValue(name, f.Name);
                    if (configValue != null)
                    {
                        var masked = Regex.Replace(configValue?.ToString(), "(...).+(...)", "$1..$2");
                        _proto.LogInfo($"- configure with \"{masked}\"");
                        f.GetSetMethod().Invoke(_config, new object[] { configValue });
                    }
                    else
                    {
                        _proto.LogInfo("- skipping null value");
                    }
                }
            }
        }

        private bool MaybeCreateWaitForWebhookDelegate(Assembly coreDll)
        {
            _webhookWaitType = coreDll.GetType(WAIT_FOR_WEBHOOK_TYPE_NAME);
            _proto.LogDebug($"Webhook wait type is null: {_webhookWaitType == null}");
            if (_webhookWaitType != null)
            {
                _webhookWaitDelegate = Delegate.CreateDelegate(_webhookWaitType, _proto, _proto.GetType().GetMethod("WaitForWebhook", BindingFlags.Public | BindingFlags.Instance));
            }
            else
            {
                _proto.LogDebug("Skipping Webhook wait delegate creation as the monitor dll doesn't have it");
            }
            _proto.LogDebug($"Webhook wait delete is null: {_webhookWaitDelegate == null}");

            return true;
        }

        private void OnExit(bool runCleanup)
        {
            var args = new object[] { _loggingDelegate };

            var tearDown = _monitorType.GetMethod("TearDown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (tearDown != null)
            {
                _proto.LogDebug("Invoking TearDown()");
                tearDown.Invoke(_monitor, args);
            }
            if (runCleanup)
            {
                var cleanup = _monitorType.GetMethod("Cleanup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cleanup != null)
                {
                    _proto.LogDebug("Invoking Cleanup()");
                    var result = cleanup.Invoke(_monitor, args);
                    if (cleanup.ReturnType == typeof(Task))
                    {
                        ((Task)result).Wait();
                    }
                }
            }
        }

        private void ExecuteStep(object monitor, MethodInfo stepFun)
        {
            var args = new object[] { _loggingDelegate };

            _proto.LogInfo($"StepFun: {stepFun}, return type is #{stepFun.ReturnType}");

            // We have four kinds of monitor steps, basically divided along two axes:
            // - synchronous and asynchronous steps (the latter mostly to make the C# code a bit more natural-looking)
            // - self-timed and externally-timed steps. Self timed steps may need to do expensive setup which we don't
            //   want to time.
            // Handle all four kinds here.

            if (stepFun.ReturnType == typeof(void))
            {
                // Just invoke and return OK. Orchestrator times.
                stepFun.Invoke(monitor, args);
                _proto.SendOK();
            }
            else if (stepFun.ReturnType == typeof(double))
            {
                // Invoke and send the measurement back.
                var result = (double)stepFun.Invoke(monitor, args);
                _proto.SendTime(result);
            }
            else if (stepFun.ReturnType == typeof(Task<double>))
            {
                // Invoke, wait, return measurement
                var result = ((Task<double>)stepFun.Invoke(monitor, args)).Result;
                _proto.SendTime(result);
            }
            else if (stepFun.ReturnType == typeof(Task))
            {
                // Invoke, wait, return OK
                ((Task)stepFun.Invoke(monitor, args)).Wait();
                _proto.SendOK();
            }
            else
            {
                _proto.LogError("Unknown return type #{stepFun.ReturnType}, skipping step");
            }
        }

        private string FindTopLevelDll(string targetDir)
        {
            // We will only have one ".deps.json" file, and that corresponds with the top level DLL
            var matches = Directory.GetFiles(targetDir, "*.deps.json");
            if (matches.Length != 1)
            {
                throw new Exception("More than one '.deps.json' found in " + targetDir);
            }
            var match = matches[0];
            _proto.LogInfo("Found match: " + match);
            return match.Replace(".deps.json", ".dll");
        }

        private void InitializeLogging()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => builder
                                         .SetMinimumLevel(MinimumLevelFromEnvironment())
                                         .AddSimpleConsole(options =>
                                         {
                                             if (Console.IsOutputRedirected)
                                             {
                                                 options.SingleLine = true;
                                                 options.ColorBehavior = LoggerColorBehavior.Disabled;
                                             }
                                         }));
            _loggerFactory = (ILoggerFactory)serviceCollection.BuildServiceProvider().GetService(typeof(ILoggerFactory));
            _logger = _loggerFactory.CreateLogger("Metrist Agent");
        }

        private LogLevel MinimumLevelFromEnvironment()
        {
            var env = Environment.GetEnvironmentVariable("METRIST_LOGGING_LEVEL") ?? "Information";
            try
            {
                return Enum.Parse<LogLevel>(env);
            }
            catch (Exception)
            {
                Console.WriteLine($"Warning: could not convert logging level {env}, running with 'Information' level.");
                return LogLevel.Information;
            }
        }

        private void SetProperty(Type type, object instance, string propertyName, object value)
        {
            var propertyInfo = type.GetProperty(propertyName);
            if (propertyInfo == null)
            {
                throw new ArgumentException($"Could not find {propertyName} on {type}. Exiting");
            }
            propertyInfo.SetValue(instance, value);
        }
    }
}
