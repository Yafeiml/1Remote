using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FarArc.Service;
using FarArc.Service.Locality;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace FarArc.Tests.Service
{
    [TestClass]
    [DoNotParallelize]
    public sealed class LocalityListViewServiceTests
    {
        private string _testDirectory = null!;
        private AppPathHelper _previousAppPath = null!;

        [TestInitialize]
        public void Initialize()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "FarArc.Tests", Guid.NewGuid().ToString("N"));
            _previousAppPath = TestInit.UseIsolatedAppPath(_testDirectory);
            LocalityListViewService.Load();
        }

        [TestCleanup]
        public void Cleanup()
        {
            LocalityListViewService.FlushPendingSave();
            AppPathHelper.Instance = _previousAppPath;
            LocalityListViewService.Load();

            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }

        [TestMethod]
        public async Task SaveAsync_CoalescesRapidChangesAndPersistsLatestSnapshot()
        {
            LocalityListViewService.Settings.ServerOrderBy = EnumServerOrderBy.NameAsc;
            LocalityListViewService.Settings.ServerCustomOrder = new Dictionary<string, int>
            {
                ["old"] = 0,
            };
            var supersededSave = LocalityListViewService.SaveAsync();

            LocalityListViewService.Settings.ServerOrderBy = EnumServerOrderBy.Custom;
            LocalityListViewService.Settings.ServerCustomOrder = new Dictionary<string, int>
            {
                ["second"] = 0,
                ["first"] = 1,
            };
            var latestSave = LocalityListViewService.SaveAsync();

            await Task.WhenAll(supersededSave, latestSave);

            Assert.IsTrue(File.Exists(LocalityListViewService.JsonPath));
            var persisted = JsonConvert.DeserializeObject<LocalityListViewSettings>(
                await File.ReadAllTextAsync(LocalityListViewService.JsonPath));
            Assert.IsNotNull(persisted);
            Assert.AreEqual(EnumServerOrderBy.Custom, persisted.ServerOrderBy);
            Assert.HasCount(2, persisted.ServerCustomOrder);
            Assert.AreEqual(0, persisted.ServerCustomOrder["second"]);
            Assert.AreEqual(1, persisted.ServerCustomOrder["first"]);
            Assert.IsFalse(persisted.ServerCustomOrder.ContainsKey("old"));
        }

        [TestMethod]
        public async Task Save_SynchronousSnapshotCannotBeOverwrittenByOlderQueuedSave()
        {
            LocalityListViewService.Settings.ServerOrderBy = EnumServerOrderBy.ProtocolAsc;
            var olderSave = LocalityListViewService.SaveAsync();

            LocalityListViewService.Settings.ServerOrderBy = EnumServerOrderBy.AddressDesc;
            LocalityListViewService.Save();
            await olderSave;

            var persisted = JsonConvert.DeserializeObject<LocalityListViewSettings>(
                await File.ReadAllTextAsync(LocalityListViewService.JsonPath));
            Assert.IsNotNull(persisted);
            Assert.AreEqual(EnumServerOrderBy.AddressDesc, persisted.ServerOrderBy);
        }
    }
}
