using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Metrist.Monitors.Nuget
{
    public class MonitorConfig : BaseMonitorConfig
    {
    }

    // Mostly based on the code samples in
    // https://docs.microsoft.com/en-us/nuget/reference/nuget-client-sdk
    // That page has more interactions available if we want to test them.
    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private NuGetVersion _versionToDownload;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
        }

        public async Task Download(Logger logger)
        {
            var cache = new SourceCacheContext();
            cache.NoCache = true;
            var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();

            string packageId = "Newtonsoft.Json";
            using MemoryStream packageStream = new MemoryStream();

            await resource.CopyNupkgToStreamAsync(
                packageId,
                _versionToDownload,
                packageStream,
                cache,
                NullLogger.Instance,
                CancellationToken.None);

            using PackageArchiveReader packageReader = new PackageArchiveReader(packageStream);
            NuspecReader nuspecReader = await packageReader.GetNuspecReaderAsync(CancellationToken.None);
        }

        public async Task ListVersions(Logger logger)
        {
            var cache = new SourceCacheContext();
            cache.NoCache = true;
            var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();

            IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
                "Newtonsoft.Json",
                cache,
                NullLogger.Instance,
                CancellationToken.None);
            var index = new Random().Next(0, versions.Count());
            _versionToDownload = versions.ElementAt(index);
        }
    }
}
