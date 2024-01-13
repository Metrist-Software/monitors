using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Metrist.Core
{
    public class Logging
    {
        private static ILoggerFactory _loggerFactory;

        /**
         * <summary>
         * While the "proper" way to do things around logging in .NET is to do dependency
         * injection, etcetera, passing ILogger around is not always simple, adds a lot of
         * noise, and we only support a console app anyway. So this lets us access a singleton
         * logger factory to obtain loggers from where needed.
         * </summary>
         */
        public static ILoggerFactory LoggerFactory
        {
            get
            {
                if (_loggerFactory == null)
                {
                    _loggerFactory = LoggerFactoryForConsole();
                }
                return _loggerFactory;
            }
        }

        public static ILogger Logger(string name) => LoggerFactory.CreateLogger(name);

        private static ILoggerFactory LoggerFactoryForConsole()
        {
            // Simplest way to setup a console logger without going all DI.
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddSimpleConsole(options =>
                {
                    if (Console.IsOutputRedirected)
                    {
                        options.TimestampFormat = "[hh:mm:ss] - ";
                        options.SingleLine = true;
                        options.ColorBehavior = LoggerColorBehavior.Disabled;
                    }
                });
            });
            return (ILoggerFactory) serviceCollection.BuildServiceProvider().GetService(typeof(ILoggerFactory));
        }
    }
}
