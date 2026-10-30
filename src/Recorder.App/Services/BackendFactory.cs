using System.Reflection;
using Recorder.Core.Abstractions;
using Recorder.App.Services;
using Recorder.App.Services.Stubs;

namespace Recorder.App.Services;

internal static class BackendFactory
{
    public static IAudioCapture CreateAudioCapture() => Create<IAudioCapture>("Recorder.Audio.Windows.WasapiAudioCapture") ?? new UnavailableAudioCapture();

    public static IRetentionService CreateRetention() => Create<IRetentionService>("Recorder.Security.RetentionService") ?? new UnavailableRetentionService();

    public static IFileProtector CreateFileProtector() => Create<IFileProtector>("Recorder.Security.FileProtector") ?? new UnavailableFileProtector();

    private static T? Create<T>(string typeName) where T : class
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is T instance)
            {
                return instance;
            }
        }

        foreach (var name in new[] { "Recorder.Audio.Windows", "Recorder.Security" })
        {
            try
            {
                var assembly = Assembly.Load(name);
                var type = assembly.GetType(typeName);
                if (type is not null && Activator.CreateInstance(type) is T instance)
                {
                    return instance;
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        return null;
    }
}
