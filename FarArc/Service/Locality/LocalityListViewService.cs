using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shawn.Utils;
using FarArc.Service.DataSource;
using FarArc.Utils.Tracing;
using FarArc.View;

namespace FarArc.Service.Locality
{
    public class LocalityListViewSettings
    {
        public EnumServerOrderBy ServerOrderBy = EnumServerOrderBy.IdAsc;
        public Dictionary<string, int> ServerCustomOrder = new Dictionary<string, int>();
        public Dictionary<string, int> GroupedOrder = new Dictionary<string, int>();
        public Dictionary<string, bool> GroupedIsExpanded = new Dictionary<string, bool>();
        public double ServerListNameWidth = 300;
        public double ServerListNoteWidth = 100;
    }

    public static class LocalityListViewService
    {
        private const int AsyncSaveDebounceMilliseconds = 150;
        private static readonly object AsyncSaveLock = new object();
        private static readonly object FileWriteLock = new object();
        private static CancellationTokenSource? _asyncSaveCancellation;
        private static long _asyncSaveRevision;
        private static long _saveRevision;

        private sealed class SaveSnapshot
        {
            public SaveSnapshot(long revision, string path, LocalityListViewSettings settings)
            {
                Revision = revision;
                Path = path;
                Settings = settings;
            }

            public long Revision { get; }
            public string Path { get; }
            public LocalityListViewSettings Settings { get; }
        }

        public static string JsonPath => Path.Combine(AppPathHelper.Instance.LocalityDirPath, ".list_view.json");
        private static LocalityListViewSettings? _settings;
        public static LocalityListViewSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    Load();
                }
                return _settings!;
            }
            private set => _settings = value;
        }


        public static void Load()
        {
            if (!File.Exists(JsonPath))
                _settings = new LocalityListViewSettings();
            try
            {
                var tmp = JsonConvert.DeserializeObject<LocalityListViewSettings>(File.ReadAllText(JsonPath));
                tmp ??= new LocalityListViewSettings();
                _settings = tmp;
            }
            catch
            {
                _settings = new LocalityListViewSettings();
            }
        }

        public static void Save()
        {
            var snapshot = CaptureSaveSnapshot();
            CancellationTokenSource? pendingSave;
            lock (AsyncSaveLock)
            {
                if (snapshot.Revision < _asyncSaveRevision)
                    return;
                pendingSave = _asyncSaveCancellation;
                _asyncSaveCancellation = null;
                _asyncSaveRevision = snapshot.Revision;
            }
            CancelNoThrow(pendingSave);
            WriteSnapshot(snapshot);
        }

        /// <summary>
        /// Debounces and writes the latest settings snapshot on a worker thread. Superseded
        /// snapshots are skipped, so rapid drag operations cannot persist an older order last.
        /// </summary>
        public static Task SaveAsync()
        {
            var snapshot = CaptureSaveSnapshot();
            var cancellation = new CancellationTokenSource();
            CancellationTokenSource? supersededSave;
            lock (AsyncSaveLock)
            {
                if (snapshot.Revision < _asyncSaveRevision)
                {
                    cancellation.Dispose();
                    return Task.CompletedTask;
                }
                supersededSave = _asyncSaveCancellation;
                _asyncSaveCancellation = cancellation;
                _asyncSaveRevision = snapshot.Revision;
            }
            CancelNoThrow(supersededSave);

            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(AsyncSaveDebounceMilliseconds, cancellation.Token).ConfigureAwait(false);
                    WriteSnapshot(snapshot);
                }
                catch (OperationCanceledException)
                {
                    // A newer snapshot (or a synchronous save) replaced this one.
                }
                catch (Exception exception)
                {
                    UnifyTracing.Error(exception);
                }
                finally
                {
                    lock (AsyncSaveLock)
                    {
                        if (ReferenceEquals(_asyncSaveCancellation, cancellation))
                            _asyncSaveCancellation = null;
                    }
                    cancellation.Dispose();
                }
            });
        }

        /// <summary>
        /// Ensures a queued drag-order save reaches disk before application shutdown.
        /// </summary>
        public static void FlushPendingSave()
        {
            lock (AsyncSaveLock)
            {
                if (_asyncSaveCancellation == null)
                    return;
            }

            Save();
        }

        private static SaveSnapshot CaptureSaveSnapshot()
        {
            var revision = Interlocked.Increment(ref _saveRevision);
            var current = Settings;
            var settings = new LocalityListViewSettings
            {
                ServerOrderBy = current.ServerOrderBy,
                ServerCustomOrder = new Dictionary<string, int>(current.ServerCustomOrder),
                GroupedOrder = new Dictionary<string, int>(current.GroupedOrder),
                GroupedIsExpanded = new Dictionary<string, bool>(current.GroupedIsExpanded),
                ServerListNameWidth = current.ServerListNameWidth,
                ServerListNoteWidth = current.ServerListNoteWidth,
            };
            return new SaveSnapshot(revision, JsonPath, settings);
        }

        private static void CancelNoThrow(CancellationTokenSource? cancellation)
        {
            try
            {
                cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The superseded worker completed between exchanging and cancelling its token.
            }
        }

        private static void WriteSnapshot(SaveSnapshot snapshot)
        {
            if (snapshot.Revision != Interlocked.Read(ref _saveRevision))
                return;

            var json = JsonConvert.SerializeObject(snapshot.Settings, Formatting.Indented);
            if (snapshot.Revision != Interlocked.Read(ref _saveRevision))
                return;

            lock (FileWriteLock)
            {
                if (snapshot.Revision != Interlocked.Read(ref _saveRevision))
                    return;

                var directory = Path.GetDirectoryName(snapshot.Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                RetryHelper.Try(
                    () => File.WriteAllText(snapshot.Path, json, Encoding.UTF8),
                    actionOnError: exception => UnifyTracing.Error(exception));
            }
        }

        public static void ServerOrderBySet(EnumServerOrderBy value)
        {
            if (Settings.ServerOrderBy == value) return;
            Settings.ServerOrderBy = value;
            Save();
        }




        public static void ServerCustomOrderSave(IEnumerable<ProtocolBaseViewModel> servers)
        {
            ServerCustomOrderUpdate(servers);
            Save();
        }

        public static Task ServerCustomOrderSaveAsync(IEnumerable<ProtocolBaseViewModel> servers)
        {
            ServerCustomOrderUpdate(servers);
            return SaveAsync();
        }

        private static void ServerCustomOrderUpdate(IEnumerable<ProtocolBaseViewModel> servers)
        {
            int i = 0;
            var orders = new Dictionary<string, int>();
            foreach (var server in servers)
            {
                orders.Add(server.Id, i);
                server.CustomOrder = i;
                ++i;
            }
            Settings.ServerCustomOrder = orders;
        }




        public static int GroupedOrderGet(string dataSourceName)
        {
            return Settings.GroupedOrder.GetValueOrDefault(dataSourceName, int.MaxValue);
        }

        public static void GroupedOrderSave(IEnumerable<string> dataSourceNames)
        {
            GroupedOrderUpdate(dataSourceNames);
            Save();
        }

        public static Task GroupedOrderSaveAsync(IEnumerable<string> dataSourceNames)
        {
            GroupedOrderUpdate(dataSourceNames);
            return SaveAsync();
        }

        private static void GroupedOrderUpdate(IEnumerable<string> dataSourceNames)
        {
            int i = 0;
            var orders = new Dictionary<string, int>();
            foreach (var str in dataSourceNames.Distinct())
            {
                orders.Add(str, i);
                ++i;
            }
            Settings.GroupedOrder = orders;
        }


        public static bool GroupedIsExpandedGet(string dataSourceName)
        {
            return Settings.GroupedIsExpanded.GetValueOrDefault(dataSourceName, true);
        }
        public static void GroupedIsExpandedSet(string dataSourceName, bool isExpanded)
        {
            try
            {
                Settings.GroupedIsExpanded[dataSourceName] = isExpanded;
                var ds = IoC.TryGet<DataSourceService>();
                if (ds != null)
                {
                    foreach (var key in Settings.GroupedIsExpanded.Keys.ToArray())
                    {
                        if (ds.LocalDataSource?.Name != key && ds.AdditionalSources.All(x => x.Key != key))
                        {
                            Settings.GroupedIsExpanded.Remove(key);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                UnifyTracing.Error(e);
                Settings.GroupedIsExpanded = new Dictionary<string, bool>();
            }
            Save();
        }

        public static void ServerListNameWidthSet(double value)
        {
            if (Math.Abs(Settings.ServerListNameWidth - value) < 0.1) return;
            Settings.ServerListNameWidth = value;
            Save();
        }

        public static void ServerListNoteWidthSet(double value)
        {
            if (Math.Abs(Settings.ServerListNoteWidth - value) < 0.1) return;
            Settings.ServerListNoteWidth = value;
            Save();
        }
    }
}
