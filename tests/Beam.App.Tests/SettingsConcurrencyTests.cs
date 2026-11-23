using Beam.Core.Display;
using Beam.Core.Settings;

namespace Beam.App.Tests;

public class SettingsConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSetters_OlderSnapshotCannotOverwriteNewerSettings()
    {
        var store = new BlockingSettingsStore();
        using var h = new PresentationHarness(Topologies.Extended(), settings: store);

        Task first = Task.Run(() => h.Manager.UseMode = false);
        await store.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task second = Task.Run(() => h.Manager.UseDpi = false);
        await Task.Delay(100); // old code lets the newer save finish before the blocked older one
        store.ReleaseFirstSave.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(store.Current.UseMode);
        Assert.False(store.Current.UseDpi);
    }

    private sealed class BlockingSettingsStore : ISettingsStore
    {
        private int _saveCalls;
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BeamSettings Current { get; private set; } = BeamSettings.Defaults;

        public event Action<string>? CorruptFileSetAside
        {
            add { }
            remove { }
        }

        public BeamSettings Load() => Current;

        public void Save(BeamSettings settings)
        {
            if (Interlocked.Increment(ref _saveCalls) == 1)
            {
                FirstSaveEntered.TrySetResult();
                ReleaseFirstSave.Task.GetAwaiter().GetResult();
            }

            Current = settings;
        }
    }
}
