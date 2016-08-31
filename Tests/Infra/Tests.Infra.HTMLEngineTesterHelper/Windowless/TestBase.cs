﻿using System;
using System.Threading.Tasks;
using MVVM.HTML.Core;
using MVVM.HTML.Core.Binding;
using MVVM.HTML.Core.Infra;
using MVVM.HTML.Core.JavascriptEngine.JavascriptObject;
using MVVM.HTML.Core.JavascriptEngine.Window;
using Tests.Infra.HTMLEngineTesterHelper.HtmlContext;
using Tests.Infra.HTMLEngineTesterHelper.Threading;
using Xunit.Abstractions;

namespace Tests.Infra.HTMLEngineTesterHelper.Windowless 
 {
    public abstract class TestBase 
    {
        protected IWebView _WebView;
        protected HTMLViewEngine _ViewEngine;
        protected IWebSessionLogger _Logger;
        private readonly IWindowlessHTMLEngineBuilder _TestEnvironment;

        protected IJavascriptObjectConverter Converter => _WebView.Converter;
        protected IJavascriptObjectFactory Factory => _WebView.Factory;

        protected TestBase(IWindowlessHTMLEngineBuilder testEnvironment, ITestOutputHelper output) 
        {
            _TestEnvironment = testEnvironment;
            var logger = new TestLogger(output);
            _Logger = logger.Add(new BasicLogger());
        }

        protected virtual void Init() 
        {
        }

        protected void Test(Action act, TestContext ipath = TestContext.Index) 
        {
            using (Tester(ipath)) 
            {
                Init();
                DoSafe(act);
            }
        }

        protected virtual IWindowlessHTMLEngine Tester(TestContext context = TestContext.Index) 
        {
            var tester = _TestEnvironment.Build();
            var path = _TestEnvironment.HtmlProvider.GetHtlmPath(context);
            _Logger.Debug($"Loading file: {path}");
            tester.Init(path, _Logger);
            _WebView = tester.WebView;
            tester.HTMLWindow.ConsoleMessage += (_, e) => _Logger.LogBrowser(e, new Uri(path));
            return tester;
        }

        public IDispatcher GetTestUIDispacther() 
        {
            return _TestEnvironment.TestUIDispacther;
        }

        protected T GetSafe<T>(Func<T> unsafeGet) 
        {
            return _WebView.EvaluateAsync(unsafeGet).Result;
        }

        protected internal Task RunInContext(Action act) 
        {
            return _WebView.RunAsync(act);
        }

        protected internal async Task RunInContext(Func<Task> act) 
        {
            var taskFactory = new DispatcherTaskFactory(_WebView);
            await await taskFactory.StartNew(act);
        }

        protected IJavascriptObject CallWithRes(IJavascriptObject value, string functionname, params IJavascriptObject[] parameter)
        {
            return _WebView.Evaluate(() => value.Invoke(functionname, _WebView, parameter));
        }

        protected void Call(IJavascriptObject value, string functionname, params IJavascriptObject[] parameter) 
        {
            _WebView.Run(() => value.Invoke(functionname, _WebView, parameter));
        }

        protected void Call(IJavascriptObject value, string functionname, Func<IJavascriptObject> parameter) 
        {
            _WebView.Run(() => value.Invoke(functionname, _WebView, parameter()));
        }

        protected IJavascriptObject Create(Func<IJavascriptObject> factory) 
        {
            return _WebView.Evaluate(factory);
        }

        protected void DoSafe(Action doact) 
        {
            _WebView.Run(doact);
        }
    }
}