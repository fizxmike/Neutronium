﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Neutronium.Core.Binding.GlueObject;
using Neutronium.Core.Binding.Listeners;
using Neutronium.Core.Exceptions;
using Neutronium.Core.Infra;
using Neutronium.Core.JavascriptFramework;
using Neutronium.Core.WebBrowserEngine.JavascriptObject;

namespace Neutronium.Core.Binding
{
    public class BidirectionalMapper : IDisposable, IVisitable, IJavascriptToCSharpConverter, IJavascriptChangesObserver   
    {
        private HTMLViewContext _Context;
        private readonly IWebSessionLogger _Logger;
        private readonly JavascriptBindingMode _BindingMode;
        private readonly CSharpToJavascriptConverter _JSObjectBuilder;
        private IJavascriptSessionInjector _sessionInjector;
        private readonly SessionCacher _SessionCache;
        private IJSCSGlue _Root;
        private readonly FullListenerRegister _ListenerRegister;
        private readonly List<IJSCSGlue> _UnrootedEntities= new List<IJSCSGlue>();
        private bool _IsListening = false;
        private bool _IsLoaded = false;

        public IJSCSGlue JSValueRoot => _Root;
        public bool ListenToCSharp => (_BindingMode != JavascriptBindingMode.OneTime);
        public JavascriptBindingMode Mode => _BindingMode;
        public HTMLViewContext Context => _Context;
        private readonly object _RootObject;

        internal BidirectionalMapper(object iRoot, HTMLViewEngine contextBuilder, JavascriptBindingMode iMode, IWebSessionLogger logger)
        {        
            _BindingMode = iMode;
            _Logger = logger;
            var javascriptObjecChanges = (iMode == JavascriptBindingMode.TwoWay) ? (IJavascriptChangesObserver)this : null;
            _Context = contextBuilder.GetMainContext(javascriptObjecChanges); 
            _SessionCache = new SessionCacher();
            _ListenerRegister = new FullListenerRegister(
                                        (n) => n.PropertyChanged += CSharpPropertyChanged,
                                        (n) => n.PropertyChanged -= CSharpPropertyChanged,
                                        (n) => n.CollectionChanged += CSharpCollectionChanged,
                                        (n) => n.CollectionChanged -= CSharpCollectionChanged,
                                        (c) => c.ListenChanges(),
                                        (c) => c.UnListenChanges());
            var commandFactory = new CommandFactory(_Context, this);
            _JSObjectBuilder = new CSharpToJavascriptConverter(contextBuilder.HTMLWindow, _SessionCache, commandFactory, _Logger) ;
            _RootObject = iRoot;
        }

        internal async Task IntrospectVm(object addicionalObject) 
        {
            await _Context.RunOnUIContextAsync(() => 
            {
                _Root = _JSObjectBuilder.Map(_RootObject, addicionalObject);

                if (ListenToCSharp)
                    ListenToCSharpChanges();

                _IsListening = true;
            });
        }

        internal async Task UpdateJavascriptObjects(bool debugMode) 
        {
            await RunInJavascriptContext(async () => 
            {
                _Context.InitOnJsContext(debugMode);
                _sessionInjector = _Context.JavascriptSessionInjector;
                RegisterJavascriptHelper();
                _Root.ComputeJavascriptValue(_Context.WebView.Factory, _SessionCache);

                var res = await InjectInHTMLSession(_Root);
                await _sessionInjector.RegisterMainViewModel(res);

                _IsLoaded = true;
            });
        }

        private void RegisterJavascriptHelper()
        {
            IJavascriptObject res;
            var resource = new ResourceReader("scripts", this);
            _Context.WebView.Eval(resource.Load("Infra.js"), out res);
        }

        private Task RunInJavascriptContext(Action run)
        {
            return _Context.WebView.RunAsync(run);
        }

        private Task RunInJavascriptContext(Func<Task> run)
        {
            return _Context.WebView.Evaluate(run);
        }

        private async Task<T> RunInJavascriptContext<T>(Func<Task<T>> run)
        {
            var res = await _Context.WebView.EvaluateAsync(run);
            return await res;
        }

        private Task<T> EvaluateInUIContextAsync<T>(Func<T> run)
        {
            return _Context.EvaluateOnUIContextAsync(run);
        }

        public void Dispose()
        {
            if (ListenToCSharp)
                UnlistenToCSharpChanges();

            _Context.Dispose();
            _UnrootedEntities.Clear();
            _Logger.Debug("BidirectionalMapper disposed");
        }

        public void Visit(IListenableObjectVisitor visitor)
        {
            _Root.ApplyOnListenable(visitor);
            _UnrootedEntities.ForEach(js => js.ApplyOnListenable(visitor));
        }

        private void ListenToCSharpChanges()
        {
            Visit(_ListenerRegister.GetOn());
        }

        private void UnlistenToCSharpChanges()
        {
            Visit(_ListenerRegister.GetOff());
        }

        private async Task<IJavascriptObject> InjectInHTMLSession(IJSCSGlue iroot)
        {
            if ( (iroot == null) || (iroot.IsBasic()))
                return null;

            var jvm = _SessionCache.GetMapper(iroot as IJSObservableBridge);
            var res = _sessionInjector.Inject(iroot.JSValue, jvm);

            if ((iroot.CValue != null) && (res==null))
            {
                throw ExceptionHelper.GetUnexpected();
            }

            await jvm.UpdateTask;
            return res;
        }

        public void OnJavaScriptObjectChanges(IJavascriptObject objectchanged, string propertyName, IJavascriptObject newValue)
        {
            try 
            {
                var res = _SessionCache.GetGlobalCached(objectchanged) as JsGenericObject;
                if (res == null)
                    return;

                var propertyAccessor = new PropertyAccessor(res.CValue, propertyName, _Logger);
                if (!propertyAccessor.IsSettable) 
                {
                    _Logger.Info(() => $"Unable to set C# from javascript object: property: {propertyName} is readonly.");
                    return;
                }

                var targetType = propertyAccessor.GetTargetType();
                var glue = GetCachedOrCreateBasic(newValue, targetType);

                Context.RunOnUIContextAsync(() => 
                {
                    using (_IsListening ? _ListenerRegister.GetPropertySilenter(res.CValue) : null) 
                    {
                        var oldValue = propertyAccessor.Get();
                        propertyAccessor.Set(glue.CValue);
                        var actualValue = propertyAccessor.Get();

                        if (Object.Equals(actualValue, glue.CValue))
                        {
                            res.UpdateGlueProperty(propertyName, glue);
                            return;
                        }

                        if (!Object.Equals(oldValue, actualValue))
                            CSharpPropertyChanged(res.CValue, new PropertyChangedEventArgs(propertyName));   
                    }
                });
            }
            catch (Exception e)
            {
                _Logger.Error(() =>$"Unable to update ViewModel from View, exception raised: {e.Message}");
            }
        }

        public void OnJavaScriptCollectionChanges(JavascriptCollectionChanges changes)
        {
            try
            {
                var res = _SessionCache.GetGlobalCached(changes.Collection) as JSArray;
                if (res == null) return;

               var collectionChanges = res.GetChanger(changes, this);

                Context.RunOnUIContextAsync(() => 
                {
                    UpdateCollection(res, collectionChanges, res.CValue);
                });
            }
            catch (Exception e)
            {
                _Logger.Error(() =>$"Unable to update ViewModel from View, exception raised: {e.Message}");
            }
        }

        private void UpdateCollection(JSArray array, Neutronium.Core.Binding.CollectionChanges.CollectionChanges change, object collection)
        {
            try
            {
                using (ReListen())
                using (_ListenerRegister.GetColllectionSilenter(collection))
                {
                    array.UpdateEventArgsFromJavascript(change);
                }
            }
            catch (Exception e)
            {
                _Logger.Error(() => $"Unable to update ViewModel from View, exception raised: {e.Message}");
            }
        }

        private async void CSharpPropertyChanged(object sender, PropertyChangedEventArgs e)
        { 
            var pn = e.PropertyName;
            var propertyAccessor = new PropertyAccessor(sender, pn, _Logger);
            if (!propertyAccessor.IsGettable)
                return;

            var currentfather = _SessionCache.GetCached(sender) as JsGenericObject;
            if (currentfather == null)
                return;

            var nv = propertyAccessor.Get();
            var oldbridgedchild = currentfather.Attributes[pn];

            if (Object.Equals(nv, oldbridgedchild.CValue))
                return;

            await UpdateFromCSharpChanges(nv, (child) => currentfather.GetUpdater(pn, child)).ConfigureAwait(false);
        }

        private async void CSharpCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            await UnsafeCSharpCollectionChanged(sender, e).ConfigureAwait(false);
        }

        private async Task UnsafeCSharpCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var arr = _SessionCache.GetCached(sender) as JSArray;
            if (arr == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    await UpdateFromCSharpChanges(e.NewItems[0], (addvalue) => arr.GetAddUpdater(addvalue, e.NewStartingIndex));
                    break;

                case NotifyCollectionChangedAction.Replace:
                    await UpdateFromCSharpChanges(e.NewItems[0], (newvalue) => arr.GetReplaceUpdater(newvalue, e.NewStartingIndex));
                    break;

                case NotifyCollectionChangedAction.Remove:
                    await UpdateFromCSharpChanges(() => arr.GetRemoveUpdater(e.OldStartingIndex), true);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    await UpdateFromCSharpChanges(() => arr.GetResetUpdater(), true);
                    break;

                case NotifyCollectionChangedAction.Move:
                    await UpdateFromCSharpChanges(() => arr.GetMoveUpdater(e.OldStartingIndex, e.NewStartingIndex), false);
                    break;
            }
        }

        public Task<IJSCSGlue> RegisterInSession(object nv)
        {
            return UpdateFromCSharpChanges(nv, GetUnrootedEntitiesUpdater);
        }

        private BridgeUpdater GetUnrootedEntitiesUpdater(IJSCSGlue newbridgedchild)
        {
            _UnrootedEntities.Add(newbridgedchild);
            return new BridgeUpdater();
        }

        private Task UpdateFromCSharpChanges(Func<BridgeUpdater> updaterBuilder, bool needToCheckListener)
        {
            CheckUIContext();

            BridgeUpdater updater = null;
            using (needToCheckListener ? ReListen(): null)
            {
                updater = updaterBuilder();
            }

            return RunInJavascriptContext(() =>
            {
                updater.UpdateJavascriptObject(_Context.ViewModelUpdater);
            });
        }

        private async Task<IJSCSGlue> UpdateFromCSharpChanges(object newCSharpObject, Func<IJSCSGlue, BridgeUpdater> updaterBuilder)
        {
            CheckUIContext();

            var value = _JSObjectBuilder.Map(newCSharpObject);
            if (value == null)
                return null;               

            BridgeUpdater updater = null;
            using (value.IsBasic()? null : ReListen())
            {
                updater = updaterBuilder(value);
            }

            if (!_IsLoaded)
                return value;

            return await RunInJavascriptContext(async () =>
            {
                value.ComputeJavascriptValue(_Context.WebView.Factory, _SessionCache);
                if (!value.IsBasic())
                {
                    await InjectInHTMLSession(value);
                }
                updater.UpdateJavascriptObject(_Context.ViewModelUpdater);
                return value;
            }).ConfigureAwait(false);
        }

        private void CheckUIContext()
        {
            if (!_Context.UIDispatcher.IsInContext())
                throw ExceptionHelper.Get("MVVM ViewModel should be updated from UI thread. Use await pattern and Dispatcher to do so.");
        }


        private ReListener _ReListen = null;

        private IDisposable ReListen()
        {
            return (_ReListen != null) ? _ReListen.AddRef() :
                        _ReListen = new ReListener(this, _ListenerRegister, () => _ReListen = null);
        }

        public IJSCSGlue GetCachedOrCreateBasic(IJavascriptObject globalkey, Type targetType)
        {
            if (globalkey == null)
                return null;

            //Use local cache for objet not created in javascript session such as enum
            var res = _SessionCache.GetCached(globalkey);
            if (res != null)
                return res;

            object targetvalue;
            bool converted = _Context.WebView.Converter.GetSimpleValue(globalkey, out targetvalue, targetType);
            if (!converted)
            {
                var message = $"Unable to convert javascript object: {globalkey} to C# session. Value will be default to null. Please check javascript bindings.";
                _Logger.Info(message);
                throw new ArgumentException(message);
            }

            return new JSBasicObject(globalkey, targetvalue);
        }
    }
}
