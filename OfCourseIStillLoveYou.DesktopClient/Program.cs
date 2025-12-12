using Avalonia;
using Avalonia.ReactiveUI;

#if OS_IS_WINDOWS
using Avalonia.Win32;
#endif

namespace OfCourseIStillLoveYou.DesktopClient
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseSkia()
#if OS_IS_WINDOWS
                .With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.Software }
                })
#endif
                .UseReactiveUI();



    }
}
