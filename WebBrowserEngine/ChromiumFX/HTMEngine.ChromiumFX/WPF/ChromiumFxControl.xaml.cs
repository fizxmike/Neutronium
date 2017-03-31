﻿using Chromium.WebBrowser;
using System.Threading.Tasks;
using System.Windows;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Neutronium.WebBrowserEngine.ChromiumFx.WPF
{
    public partial class ChromiumFxControl
    {
        internal ChromiumWebBrowser WebBrowser => ChromiumWebBrowser;
        private Window Window { get; set; }
        private IntPtr WindowHandle { get; set; }
        private IntPtr FormHandle => _BrowserHandle;
        private IntPtr _ChromeWidgetHostHandle;
        private System.Windows.Point dragOffset = new System.Windows.Point();
        private bool _Dragging = false;
        private BrowserWidgetMessageInterceptor _ChromeWidgetMessageInterceptor;
        private Region draggableRegion = null;

        public ChromiumFxControl()
        {
            InitializeComponent();
            this.Loaded += ChromiumFxControl_Loaded;
            ChromiumWebBrowser.BrowserCreated += ChromiumWebBrowser_BrowserCreated;
            var dragHandler = ChromiumWebBrowser.DragHandler;
            dragHandler.OnDragEnter += (o, e) => { e.SetReturnValue(true); };
            dragHandler.OnDraggableRegionsChanged += DragHandler_OnDraggableRegionsChanged;
        }

        private void DragHandler_OnDraggableRegionsChanged(object sender, Chromium.Event.CfxOnDraggableRegionsChangedEventArgs args)
        {
            draggableRegion = args.Regions.Aggregate(draggableRegion, (current, region) =>
            {
                var rect = new Rectangle(region.Bounds.X, region.Bounds.Y, region.Bounds.Width, region.Bounds.Height);
                if (current == null)
                    return new Region(rect);

                if (region.Draggable)
                    current.Union(rect);
                else
                    current.Exclude(rect);

                return current;
            });
        }

        private IntPtr _BrowserHandle;
        private async void ChromiumWebBrowser_BrowserCreated(object sender, Chromium.WebBrowser.Event.BrowserCreatedEventArgs e)
        {
            _BrowserHandle = e.Browser.Host.WindowHandle;
            await Task.Delay(1000);
            
            if (ChromeWidgetHandleFinder.TryFindHandle(_BrowserHandle, out _ChromeWidgetHostHandle))
                _ChromeWidgetMessageInterceptor = new BrowserWidgetMessageInterceptor(this.ChromiumWebBrowser, _ChromeWidgetHostHandle, OnWebBroswerMessage);
        }

        private void ChromiumFxControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= ChromiumFxControl_Loaded;
            Window = Window.GetWindow(this);
            WindowHandle = new System.Windows.Interop.WindowInteropHelper(Window).Handle;
            Window.StateChanged += Window_StateChanged;
            Window.Closed += Window_Closed;
        }

        private async void Window_StateChanged(object sender, EventArgs e)
        {
            if (Window.WindowState == WindowState.Minimized)
                return;

            await Task.Delay(10);
            ChromiumWebBrowser.Refresh();
        }

        private bool OnWebBroswerMessage(Message message)
        {
            switch (message.Msg)
            {
                case NativeMethods.WindowsMessage.WM_MOUSEACTIVATE:
                    var topLevelWindowHandle = message.WParam;
                    NativeMethods.PostMessage(topLevelWindowHandle, NativeMethods.WindowsMessage.WM_NCLBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
                    break;

                case NativeMethods.WindowsMessage.WM_LBUTTONDOWN:
                    var point = GetPoint(message.LParam);
                    var dragable = draggableRegion?.IsVisible(Convert(point)) == true;

                    if (!dragable)
                        break;

                    NativeMethods.PostMessage(FormHandle, NativeMethods.WindowsMessage.WM_LBUTTONDOWN, message.WParam, message.LParam);
                    Dispatcher.Invoke(() => DragInit(point));
                    //NativeMethods.SendMessage(_ChromeWidgetHostHandle, NativeMethods.WindowsMessage.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HitTest.HTCAPTION, (IntPtr)0);
                    return true;

                case NativeMethods.WindowsMessage.WM_LBUTTONUP:
                    _Dragging = false;
                    break;

                case NativeMethods.WindowsMessage.WM_MOUSEMOVE:                  
                    _Dragging = _Dragging && (int)message.WParam == NativeMethods.windowsParam.MK_LBUTTON;

                    if (_Dragging)
                        Dispatcher.Invoke(() => OnMouseDown(GetPoint(message.LParam)));

                    NativeMethods.SendMessage(FormHandle, NativeMethods.WindowsMessage.WM_MOUSEMOVE, message.WParam, message.LParam);
                    break;
            }
            return false;
        }

        private System.Windows.Point GetPoint(IntPtr lparam)
        {
            var x = NativeMethods.LoWord(lparam.ToInt32());
            var y = NativeMethods.HiWord(lparam.ToInt32());
            return new System.Windows.Point(x, y);
        }

        private System.Drawing.Point Convert(System.Windows.Point point)
        {
            return new System.Drawing.Point((int)point.X, (int)point.Y);
        }

        private System.Windows.Point RealPixelsToWpf(System.Windows.Point point)
        {
            return Window.PointToScreen(point);
        }

        private void DragInit(System.Windows.Point point)
        {
            _Dragging = true;
            var off = RealPixelsToWpf(point);
            dragOffset = new System.Windows.Point(Window.Left - off.X, Window.Top - off.Y);
        }

        protected void OnMouseDown(System.Windows.Point point)
        {
            if (!_Dragging)
                return;

            var off = RealPixelsToWpf(point);
            Window.Left = dragOffset.X + off.X;
            Window.Top = dragOffset.Y + off.Y;
        }

        private void Window_Closed(object sender, System.EventArgs e)
        {
            Window.Closed -= Window_Closed;
            Window.StateChanged -= Window_StateChanged;

            _ChromeWidgetMessageInterceptor?.ReleaseHandle();
            _ChromeWidgetMessageInterceptor?.DestroyHandle();
            _ChromeWidgetMessageInterceptor = null;
        }
    }
}
