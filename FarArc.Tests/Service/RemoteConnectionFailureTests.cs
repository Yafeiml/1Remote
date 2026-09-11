using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FarArc.Model.Protocol;
using FarArc.Service;
using FarArc.View.Host.ProtocolHosts;
using FarArc.View.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FarArc.Tests.Service;

[TestClass]
[DoNotParallelize]
public sealed class RemoteConnectionFailureTests
{
    [STATestMethod]
    public void FailedConnections_KeepErrorPageUntilExplicitClose()
    {
        // Exercise the real WPF hosts and native event handlers without contacting a server.
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previousBusyStyle = application.Resources["BusyAnimationStyle"];
        var previousButtonStyle = application.Resources["ButtonPrimaryStyle"];
        application.Resources["BusyAnimationStyle"] = new Style(typeof(Control));
        application.Resources["ButtonPrimaryStyle"] = new Style(typeof(Button));
        var previousResolver = IoC.GetByType;
        var settings = new SettingsPageViewModel(null!, null!);
        var configuration = new ConfigurationService(new KeywordMatchService(), new Configuration());
        IoC.GetByType = (type, key) => type == typeof(SettingsPageViewModel) ? settings
            : type == typeof(ConfigurationService) ? configuration : previousResolver(type, key);
        try
        {
            foreach (var code in new[] { 2, 3, 260, 264, 516 })
            foreach (var transportConnected in new[] { false, true })
            {
                using var rdp = Create<AxMsRdpClient09Host>(new RDP
                {
                    Id = "connection-failure-test",
                    DisplayName = "Unreachable test host",
                    Address = "127.0.0.1",
                    Port = "1",
                }, 800, 600);
                Assert.AreEqual(ProtocolHostStatus.Initialized, rdp.Status);
                var client = Field<object>(rdp, "_rdpClient");
                var closeCount = 0;
                rdp.OnClosed += _ => closeCount++;
                if (transportConnected)
                    Invoke(rdp, "OnRdpClientConnected", client, EventArgs.Empty);
                // Simulate the text state left by an automatic retry.
                Field<TextBlock>(rdp, "TbMessage").Visibility = Visibility.Collapsed;
                Disconnect(rdp, client, code);
                AssertFailurePage(rdp);
                StringAssert.Contains(Field<TextBlock>(rdp, "TbMessage").Text, $"RDP: {code}");
                Assert.AreEqual(0, closeCount, $"Initial failure {code} must not close the host.");
                Assert.AreEqual(transportConnected, rdp.HasConnected);
                rdp.Close();
                Assert.AreEqual(1, closeCount);
            }

            foreach (var normalLogoff in new[] { false, true })
            {
                using var rdp = Create<AxMsRdpClient09Host>(new RDP
                {
                    Id = "established-test", DisplayName = "Established test", Address = "127.0.0.1", Port = "1",
                }, 800, 600);
                var client = Field<object>(rdp, "_rdpClient");
                var closeCount = 0;
                rdp.OnClosed += _ => closeCount++;
                Invoke(rdp, "OnRdpClientConnected", client, EventArgs.Empty);
                Assert.AreEqual(ProtocolHostStatus.Connected, rdp.Status);
                Invoke(rdp, "OnRdpClientLoginComplete", client, EventArgs.Empty);
                if (normalLogoff)
                {
                    Disconnect(rdp, client, 2);
                    Assert.AreEqual(1, closeCount, "Normal logoff must still close an established session.");
                }
                else
                {
                    typeof(AxMsRdpClient09Host).GetField("_retryCount", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(rdp, 5);
                    Field<TextBlock>(rdp, "TbMessage").Visibility = Visibility.Collapsed;
                    Disconnect(rdp, client, 516);
                    AssertFailurePage(rdp);
                    Assert.AreEqual(0, closeCount, "Exhausted retries must retain the error page.");
                }
            }

            var vnc = Create<VncHost>(new VNC
            {
                Id = "vnc-failure-test", DisplayName = "VNC test", Address = "127.0.0.1", Port = "1",
            });
            try
            {
                var closeCount = 0;
                vnc.OnClosed += _ => closeCount++;
                Invoke(vnc, "OnConnectionLost", null, EventArgs.Empty);
                AssertFailurePage(vnc);
                Assert.AreEqual(0, closeCount);
                Assert.IsFalse(vnc.HasConnected);
                // A lost established connection should also retain the retry page.
                Invoke(vnc, "OnConnected", vnc, EventArgs.Empty);
                Invoke(vnc, "OnConnectionLost", null, EventArgs.Empty);
                AssertFailurePage(vnc);
                Assert.AreEqual(0, closeCount);
                vnc.Close();
                Assert.AreEqual(1, closeCount);
            }
            finally
            {
                vnc.Close();
                Field<System.Windows.Forms.Integration.WindowsFormsHost>(vnc, "VncFormsHost").Dispose();
            }
        }
        finally
        {
            IoC.GetByType = previousResolver;
            if (previousBusyStyle == null) application.Resources.Remove("BusyAnimationStyle");
            else application.Resources["BusyAnimationStyle"] = previousBusyStyle;
            if (previousButtonStyle == null) application.Resources.Remove("ButtonPrimaryStyle");
            else application.Resources["ButtonPrimaryStyle"] = previousButtonStyle;
        }
    }

    private static void Disconnect(AxMsRdpClient09Host host, object client, int code)
    {
        var eventType = typeof(AxMsRdpClient09Host).GetMethod("OnRdpClientDisconnected",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetParameters()[1].ParameterType;
        Invoke(host, "OnRdpClientDisconnected", client, Activator.CreateInstance(eventType, code));
    }

    private static T Create<T>(params object[] arguments) => (T)Activator.CreateInstance(
        typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)!;

    private static T Field<T>(object host, string name) => (T)host.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(host)!;

    private static void Invoke(object host, string name, params object?[] arguments) => host.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, arguments);

    private static void AssertFailurePage(HostBase host)
    {
        Assert.AreEqual(ProtocolHostStatus.Disconnected, host.Status);
        Assert.AreEqual(Visibility.Collapsed, Field<Control>(host, "GridLoading").Visibility);
        Assert.AreEqual(Visibility.Visible, Field<Grid>(host, "GridMessageBox").Visibility);
        Assert.AreEqual(Visibility.Visible, Field<TextBlock>(host, "TbMessage").Visibility);
        Assert.IsFalse(string.IsNullOrWhiteSpace(Field<TextBlock>(host, "TbMessage").Text));
        Assert.AreEqual(Visibility.Visible, Field<Button>(host, "BtnReconn").Visibility);
    }
}
