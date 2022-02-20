using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows;
using BililiveRecorder.Core.Api.Http;
using Newtonsoft.Json.Linq;
using Serilog;

#nullable enable
namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for AdvancedSettingsPage.xaml
    /// </summary>
    public partial class AdvancedSettingsPage : IPage
    {
        private static readonly ILogger logger = Log.ForContext<AdvancedSettingsPage>();
        private readonly HttpApiClient? httpApiClient;

        public AdvancedSettingsPage(HttpApiClient? httpApiClient)
        {
            this.InitializeComponent();
            this.httpApiClient = httpApiClient;
        }
#if DEBUG
        public AdvancedSettingsPage() : this(null)
        {
        }
#endif

        private void Crash_Click(object sender, RoutedEventArgs e) => throw new TestException("test crash triggered");

        public class TestException : Exception
        {
            public TestException() { }
            public TestException(string message) : base(message) { }
            public TestException(string message, Exception innerException) : base(message, innerException) { }
            protected TestException(SerializationInfo info, StreamingContext context) : base(info, context) { }
        }

        private void Throw_In_Task_Click(object sender, RoutedEventArgs e) => _ = Task.Run(() =>
        {
            throw new TestException("test task exception triggered");
        });

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void TestCookie_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            try
            {
                await this.TestCookieAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Exception in TestCookie");
                MessageBox.Show(ex.ToString(), "Cookie Test - Error", MessageBoxButton.OK);
            }
        }

        private async Task TestCookieAsync()
        {
            if (this.httpApiClient is null)
            {
                MessageBox.Show("No Http Client Available", "Cookie Test - Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resp = await this.httpApiClient.MainHttpClient.GetStringAsync("https://api.live.bilibili.com/xlive/web-ucenter/user/get_user_info").ConfigureAwait(false);
            var jo = JObject.Parse(resp);
            if (jo["code"]?.ToObject<int>() != 0)
            {
                MessageBox.Show("Response:\n" + resp, "Cookie Test - Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("User: " + jo["data"]?["uname"]?.ToObject<string>(), "Cookie Test - Successed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
