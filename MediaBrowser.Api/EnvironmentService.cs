using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Class GetDirectoryContents
    /// </summary>
    [Route("/Environment/DirectoryContents", "GET", Summary = "Gets the contents of a given directory in the file system")]
    public class GetDirectoryContents : IReturn<List<FileSystemEntryInfo>>
    {
        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        /// <value>The path.</value>
        [ApiMember(Name = "Path", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [include files].
        /// </summary>
        /// <value><c>true</c> if [include files]; otherwise, <c>false</c>.</value>
        [ApiMember(Name = "IncludeFiles", Description = "An optional filter to include or exclude files from the results. true/false", IsRequired = false, DataType = "boolean", ParameterType = "query", Verb = "GET")]
        public bool IncludeFiles { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [include directories].
        /// </summary>
        /// <value><c>true</c> if [include directories]; otherwise, <c>false</c>.</value>
        [ApiMember(Name = "IncludeDirectories", Description = "An optional filter to include or exclude folders from the results. true/false", IsRequired = false, DataType = "boolean", ParameterType = "query", Verb = "GET")]
        public bool IncludeDirectories { get; set; }
    }

    [Route("/Environment/ValidatePath", "POST", Summary = "Gets the contents of a given directory in the file system")]
    public class ValidatePath
    {
        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        /// <value>The path.</value>
        [ApiMember(Name = "Path", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Path { get; set; }

        public bool ValidateWriteable { get; set; }
        public bool? IsFile { get; set; }
    }

    [Route("/Environment/NetworkShares", "GET", Summary = "Gets shares from a network device")]
    public class GetNetworkShares : IReturn<List<FileSystemEntryInfo>>
    {
        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        /// <value>The path.</value>
        [ApiMember(Name = "Path", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Class GetDrives
    /// </summary>
    [Route("/Environment/Drives", "GET", Summary = "Gets available drives from the server's file system")]
    public class GetDrives : IReturn<List<FileSystemEntryInfo>>
    {
    }

    /// <summary>
    /// Class GetNetworkComputers
    /// </summary>
    [Route("/Environment/NetworkDevices", "GET", Summary = "Gets a list of devices on the network")]
    public class GetNetworkDevices : IReturn<List<FileSystemEntryInfo>>
    {
    }

    [Route("/Environment/ParentPath", "GET", Summary = "Gets the parent path of a given path")]
    public class GetParentPath : IReturn<string>
    {
        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        /// <value>The path.</value>
        [ApiMember(Name = "Path", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string Path { get; set; }
    }

    public class DefaultDirectoryBrowserInfo
    {
        public string Path { get; set; }
    }

    [Route("/Environment/DefaultDirectoryBrowser", "GET", Summary = "Gets the parent path of a given path")]
    public class GetDefaultDirectoryBrowser : IReturn<DefaultDirectoryBrowserInfo>
    {
        
    }

    /// <summary>
    /// Class EnvironmentService
    /// </summary>
    [Authenticated(Roles = "Admin", AllowBeforeStartupWizard = true)]
    public class EnvironmentService : BaseApiService
    {
        const char UncSeparator = '\\';
        const string UncSeparatorString = "\\";

        /// <summary>
        /// The _network manager
        /// </summary>
        private readonly INetworkManager _networkManager;
        private readonly IFileSystem _fileSystem;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnvironmentService" /> class.
        /// </summary>
        /// <param name="networkManager">The network manager.</param>
        public EnvironmentService(INetworkManager networkManager, IFileSystem fileSystem)
        {
            if (networkManager == null)
            {
                throw new ArgumentNullException("networkManager");
            }

            _networkManager = networkManager;
            _fileSystem = fileSystem;
        }

        public void Post(ValidatePath request)
        {
            if (request.IsFile.HasValue)
            {
                if (request.IsFile.Value)
                {
                    if (!_fileSystem.FileExists(request.Path))
                    {
                        throw new FileNotFoundException("File not found", request.Path);
                    }
                }
                else
                {
                    if (!_fileSystem.DirectoryExists(request.Path))
                    {
                        throw new FileNotFoundException("File not found", request.Path);
                    }
                }
            }

            else
            {
                if (!_fileSystem.FileExists(request.Path) && !_fileSystem.DirectoryExists(request.Path))
                {
                    throw new FileNotFoundException("Path not found", request.Path);
                }

                if (request.ValidateWriteable)
                {
                    EnsureWriteAccess(request.Path);
                }
            }
        }

        protected void EnsureWriteAccess(string path)
        {
            var file = Path.Combine(path, Guid.NewGuid().ToString());

            _fileSystem.WriteAllText(file, string.Empty);
            _fileSystem.DeleteFile(file);
        }

        public object Get(GetDefaultDirectoryBrowser request)
        {
            var result = new DefaultDirectoryBrowserInfo();

            result.Path = _fileSystem.DefaultDirectory;

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetDirectoryContents request)
        {
            var path = request.Path;

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("Path");
            }

            var networkPrefix = UncSeparatorString + UncSeparatorString;

            if (path.StartsWith(networkPrefix, StringComparison.OrdinalIgnoreCase) && path.LastIndexOf(UncSeparator) == 1)
            {
                return ToOptimizedResult(GetNetworkShares(path).OrderBy(i => i.Path).ToList());
            }

            return ToOptimizedResult(GetFileSystemEntries(request).ToList());
        }

        public object Get(GetNetworkShares request)
        {
            var path = request.Path;

            var shares = GetNetworkShares(path).OrderBy(i => i.Path).ToList();

            return ToOptimizedResult(shares);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetDrives request)
        {
            var result = GetDrives().ToList();

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the list that is returned when an empty path is supplied
        /// </summary>
        /// <returns>IEnumerable{FileSystemEntryInfo}.</returns>
        private IEnumerable<FileSystemEntryInfo> GetDrives()
        {
            return _fileSystem.GetDrives().Select(d => new FileSystemEntryInfo
            {
                Name = d.Name,
                Path = d.FullName,
                Type = FileSystemEntryType.Directory
            });
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetNetworkDevices request)
        {
            var result = _networkManager.GetNetworkDevices().ToList();

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the network shares.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>IEnumerable{FileSystemEntryInfo}.</returns>
        private IEnumerable<FileSystemEntryInfo> GetNetworkShares(string path)
        {
            return _networkManager.GetNetworkShares(path).Where(s => s.ShareType == NetworkShareType.Disk).Select(c => new FileSystemEntryInfo
            {
                Name = c.Name,
                Path = Path.Combine(path, c.Name),
                Type = FileSystemEntryType.NetworkShare
            });
        }

        /// <summary>
        /// Gets the file system entries.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>IEnumerable{FileSystemEntryInfo}.</returns>
        private IEnumerable<FileSystemEntryInfo> GetFileSystemEntries(GetDirectoryContents request)
        {
            var entries = _fileSystem.GetFileSystemEntries(request.Path).OrderBy(i => i.FullName).Where(i =>
            {
                var isDirectory = i.IsDirectory;

                if (!request.IncludeFiles && !isDirectory)
                {
                    return false;
                }

                if (!request.IncludeDirectories && isDirectory)
                {
                    return false;
                }

                return true;
            });

            return entries.Select(f => new FileSystemEntryInfo
            {
                Name = f.Name,
                Path = f.FullName,
                Type = f.IsDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File

            });
        }

        public object Get(GetParentPath request)
        {
            var parent = _fileSystem.GetDirectoryName(request.Path);

            if (string.IsNullOrEmpty(parent))
            {
                // Check if unc share
                var index = request.Path.LastIndexOf(UncSeparator);

                if (index != -1 && request.Path.IndexOf(UncSeparator) == 0)
                {
                    parent = request.Path.Substring(0, index);

                    if (string.IsNullOrWhiteSpace(parent.TrimStart(UncSeparator)))
                    {
                        parent = null;
                    }
                }
            }

            return parent;
        }
    }
}
