using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Net;

namespace Emby.Server.Implementations.Images
{
    public abstract class BaseDynamicImageProvider<T> : IHasItemChangeMonitor, IForcedProvider, ICustomMetadataProvider<T>, IHasOrder
        where T : BaseItem
    {
        protected IFileSystem FileSystem { get; private set; }
        protected IProviderManager ProviderManager { get; private set; }
        protected IApplicationPaths ApplicationPaths { get; private set; }
        protected IImageProcessor ImageProcessor { get; set; }

        protected BaseDynamicImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor)
        {
            ApplicationPaths = applicationPaths;
            ProviderManager = providerManager;
            FileSystem = fileSystem;
            ImageProcessor = imageProcessor;
        }

        protected virtual bool Supports(BaseItem item)
        {
            return true;
        }

        public virtual ImageType[] GetSupportedImages(BaseItem item)
        {
            return new ImageType[]
            {
                ImageType.Primary
            };
        }

        public async Task<ItemUpdateType> FetchAsync(T item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            if (!Supports(item))
            {
                return ItemUpdateType.None;
            }

            var updateType = ItemUpdateType.None;
            var supportedImages = GetSupportedImages(item);

            if (supportedImages.Contains(ImageType.Primary))
            {
                var primaryResult = await FetchAsync(item, ImageType.Primary, options, cancellationToken).ConfigureAwait(false);
                updateType = updateType | primaryResult;
            }

            if (supportedImages.Contains(ImageType.Thumb))
            {
                var thumbResult = await FetchAsync(item, ImageType.Thumb, options, cancellationToken).ConfigureAwait(false);
                updateType = updateType | thumbResult;
            }

            return updateType;
        }

        protected Task<ItemUpdateType> FetchAsync(BaseItem item, ImageType imageType, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var image = item.GetImageInfo(imageType, 0);

            if (image != null)
            {
                if (!image.IsLocalFile)
                {
                    return Task.FromResult(ItemUpdateType.None);
                }

                if (!FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), image.Path))
                {
                    return Task.FromResult(ItemUpdateType.None);
                }
            }

            var items = GetItemsWithImages(item);

            return FetchToFileInternal(item, items, imageType, cancellationToken);
        }

        protected async Task<ItemUpdateType> FetchToFileInternal(BaseItem item,
            List<BaseItem> itemsWithImages,
            ImageType imageType,
            CancellationToken cancellationToken)
        {
            var outputPathWithoutExtension = Path.Combine(ApplicationPaths.TempDirectory, Guid.NewGuid().ToString("N"));
            FileSystem.CreateDirectory(FileSystem.GetDirectoryName(outputPathWithoutExtension));
            string outputPath = CreateImage(item, itemsWithImages, outputPathWithoutExtension, imageType, 0);

            if (string.IsNullOrEmpty(outputPath))
            {
                return ItemUpdateType.None;
            }

            var mimeType = MimeTypes.GetMimeType(outputPath);

            if (string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                mimeType = "image/png";
            }

            await ProviderManager.SaveImage(item, outputPath, mimeType, imageType, null, false, cancellationToken).ConfigureAwait(false);

            return ItemUpdateType.ImageUpdate;
        }

        protected abstract List<BaseItem> GetItemsWithImages(BaseItem item);

        protected string CreateThumbCollage(BaseItem primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 640, 360);
        }

        protected virtual IEnumerable<string> GetStripCollageImagePaths(BaseItem primaryItem, IEnumerable<BaseItem> items)
        {
            return items
                .Select(i =>
                {
                    var image = i.GetImageInfo(ImageType.Primary, 0);

                    if (image != null && image.IsLocalFile)
                    {
                        return image.Path;
                    }
                    image = i.GetImageInfo(ImageType.Thumb, 0);

                    if (image != null && image.IsLocalFile)
                    {
                        return image.Path;
                    }
                    return null;
                })
                .Where(i => !string.IsNullOrEmpty(i));
        }

        protected string CreatePosterCollage(BaseItem primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 400, 600);
        }

        protected string CreateSquareCollage(BaseItem primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 600, 600);
        }

        protected string CreateThumbCollage(BaseItem primaryItem, List<BaseItem> items, string outputPath, int width, int height)
        {
            return CreateCollage(primaryItem, items, outputPath, width, height);
        }

        private string CreateCollage(BaseItem primaryItem, List<BaseItem> items, string outputPath, int width, int height)
        {
            FileSystem.CreateDirectory(FileSystem.GetDirectoryName(outputPath));

            var options = new ImageCollageOptions
            {
                Height = height,
                Width = width,
                OutputPath = outputPath,
                InputPaths = GetStripCollageImagePaths(primaryItem, items).ToArray()
            };

            if (options.InputPaths.Length == 0)
            {
                return null;
            }

            if (!ImageProcessor.SupportsImageCollageCreation)
            {
                return null;
            }

            ImageProcessor.CreateImageCollage(options);
            return outputPath;
        }

        public string Name
        {
            get { return "Dynamic Image Provider"; }
        }

        protected virtual string CreateImage(BaseItem item,
            List<BaseItem> itemsWithImages,
            string outputPathWithoutExtension,
            ImageType imageType,
            int imageIndex)
        {
            if (itemsWithImages.Count == 0)
            {
                return null;
            }

            string outputPath = Path.ChangeExtension(outputPathWithoutExtension, ".png");

            if (imageType == ImageType.Thumb)
            {
                return CreateThumbCollage(item, itemsWithImages, outputPath);
            }

            if (imageType == ImageType.Primary)
            {
                if (item is UserView)
                {
                    return CreateSquareCollage(item, itemsWithImages, outputPath);
                }
                if (item is Playlist || item is MusicGenre || item is Genre || item is GameGenre || item is PhotoAlbum)
                {
                    return CreateSquareCollage(item, itemsWithImages, outputPath);
                }
                return CreatePosterCollage(item, itemsWithImages, outputPath);
            }

            throw new ArgumentException("Unexpected image type");
        }

        protected virtual int MaxImageAgeDays
        {
            get { return 7; }
        }

        public bool HasChanged(BaseItem item, IDirectoryService directoryServicee)
        {
            if (!Supports(item))
            {
                return false;
            }

            var supportedImages = GetSupportedImages(item);

            if (supportedImages.Contains(ImageType.Primary) && HasChanged(item, ImageType.Primary))
            {
                return true;
            }
            if (supportedImages.Contains(ImageType.Thumb) && HasChanged(item, ImageType.Thumb))
            {
                return true;
            }

            return false;
        }

        protected bool HasChanged(BaseItem item, ImageType type)
        {
            var image = item.GetImageInfo(type, 0);

            if (image != null)
            {
                if (!image.IsLocalFile)
                {
                    return false;
                }

                if (!FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), image.Path))
                {
                    return false;
                }

                if (!HasChangedByDate(item, image))
                {
                    return false;
                }
            }

            return true;
        }

        protected virtual bool HasChangedByDate(BaseItem item, ItemImageInfo image)
        {
            var age = DateTime.UtcNow - image.DateModified;
            if (age.TotalDays <= MaxImageAgeDays)
            {
                return false;
            }
            return true;
        }

        public int Order
        {
            get
            {
                // Run before the default image provider which will download placeholders
                return 0;
            }
        }

        protected string CreateSingleImage(List<BaseItem> itemsWithImages, string outputPathWithoutExtension, ImageType imageType)
        {
            var image = itemsWithImages
                .Where(i => i.HasImage(imageType) && i.GetImageInfo(imageType, 0).IsLocalFile && Path.HasExtension(i.GetImagePath(imageType)))
                .Select(i => i.GetImagePath(imageType))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(image))
            {
                return null;
            }

            var ext = Path.GetExtension(image);

            var outputPath = Path.ChangeExtension(outputPathWithoutExtension, ext);
            FileSystem.CopyFile(image, outputPath, true);

            return outputPath;
        }
    }
}
