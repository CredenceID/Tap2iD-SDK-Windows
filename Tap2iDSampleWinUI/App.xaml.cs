using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Tap2iDSampleWinUI.Services;
using Tap2iDSampleWinUI.ViewModels;
using Tap2iDSdk;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tap2iDSampleWinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Composition root. Services and view models are resolved from here so the
        /// SDK verifier is created once and shared across the peripheral service and UI.
        /// </summary>
        public static IServiceProvider Services { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();

            // A slip in UI code (e.g. a callback queued on the dispatcher) must not end a QA run.
            // Log it to the Tap2iD log through the SDK's Serilog logger and keep the app running.
            this.UnhandledException += (_, e) =>
            {
                Serilog.Log.Error(e.Exception, "Unhandled UI exception (app kept running): {Message}", e.Message);
                System.Diagnostics.Debug.WriteLine($"Unhandled UI exception: {e.Exception}");
                e.Handled = true;
            };
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Single shared SDK verifier instance (created lazily on first resolve).
            services.AddSingleton<IVerifyMdoc>(_ => VerifyMdocFactory.CreateVerifyMdoc(true));

            // Peripheral API behind a clean, injectable boundary.
            services.AddSingleton<IPeripheralService, PeripheralService>();

            // Presentation state for the per-capability reader selection UI.
            services.AddSingleton<PeripheralSelectionViewModel>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }

        private Window m_window;
    }
}
