﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Broadcaster.UI.MainWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using NLog.Config;
using NLog.Targets;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Broadcaster
{
    public class Bootstrapper : BootstrapperBase
    {
        private readonly SimpleContainer _simpleContainer = new SimpleContainer();

        public Bootstrapper()
        {
            Initialize();
            SetupLogging();

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

          //  Analytics.Log("Server", "Startup", Guid.NewGuid().ToString());
        }

        private void SetupLogging()
        {  
            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            fileTarget.FileName = "${basedir}/broadcaster-log.txt";
            fileTarget.Layout =
                @"${longdate} | ${logger} | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=1}";

#if DEBUG
            config.LoggingRules.Add( new LoggingRule("*", LogLevel.Debug, fileTarget));
#else
            config.LoggingRules.Add( new LoggingRule("*", LogLevel.Info, fileTarget));
#endif
           
            LogManager.Configuration = config;
        }


        protected override void Configure()
        {
            _simpleContainer.Singleton<IWindowManager, WindowManager>();
            _simpleContainer.Singleton<IEventAggregator, EventAggregator>();
         

            _simpleContainer.Singleton<MainViewModel>();

        }

        protected override object GetInstance(Type service, string key)
        {
            var instance = _simpleContainer.GetInstance(service, key);
            if (instance != null)
                return instance;

            throw new InvalidOperationException("Could not locate any instances.");
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _simpleContainer.GetAllInstances(service);
        }


        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            IDictionary<string, object> settings = new Dictionary<string, object>
            {
              //  {"Icon", new BitmapImage(new Uri("pack://application:,,,/SR-Server;component/server-10.ico"))},
                {"ResizeMode", ResizeMode.CanMinimize}
            };
            //create an instance of serverState to actually start the server
         //   _simpleContainer.GetInstance(typeof(ServerState), null);

            DisplayRootViewFor<MainViewModel>(settings);

            UpdaterChecker.CheckForUpdate();
        }

        protected override void BuildUp(object instance)
        {
            _simpleContainer.BuildUp(instance);
        }


        protected override void OnExit(object sender, EventArgs e)
        {
//            var serverState = (ServerState) _simpleContainer.GetInstance(typeof(ServerState), null);
           
        }
    }
}