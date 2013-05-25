﻿using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.IO;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MediaBrowser.Controller.Providers.Music
{
    /// <summary>
    /// Class FanArtArtistProvider
    /// </summary>
    public class FanArtArtistProvider : FanartBaseProvider
    {
        /// <summary>
        /// Gets the HTTP client.
        /// </summary>
        /// <value>The HTTP client.</value>
        protected IHttpClient HttpClient { get; private set; }

        private readonly IProviderManager _providerManager;

        public FanArtArtistProvider(IHttpClient httpClient, ILogManager logManager, IServerConfigurationManager configurationManager, IProviderManager providerManager)
            : base(logManager, configurationManager)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException("httpClient");
            }
            HttpClient = httpClient;
            _providerManager = providerManager;
        }

        /// <summary>
        /// The fan art base URL
        /// </summary>
        protected string FanArtBaseUrl = "http://api.fanart.tv/webservice/artist/{0}/{1}/xml/all/1/1";

        /// <summary>
        /// Supportses the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        public override bool Supports(BaseItem item)
        {
            return item is MusicArtist;
        }

        protected virtual bool SaveLocalMeta
        {
            get { return ConfigurationManager.Configuration.SaveLocalMeta; }
        }

        protected override bool RefreshOnVersionChange
        {
            get
            {
                return true;
            }
        }

        protected override string ProviderVersion
        {
            get
            {
                return "5";
            }
        }

        /// <summary>
        /// Needses the refresh internal.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="providerInfo">The provider info.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        protected override bool NeedsRefreshInternal(BaseItem item, BaseProviderInfo providerInfo)
        {
            if (string.IsNullOrEmpty(item.GetProviderId(MetadataProviders.Musicbrainz)))
            {
                return false;
            }

            if (!ConfigurationManager.Configuration.DownloadMusicArtistImages.Art &&
                !ConfigurationManager.Configuration.DownloadMusicArtistImages.Backdrops &&
                !ConfigurationManager.Configuration.DownloadMusicArtistImages.Banner &&
                !ConfigurationManager.Configuration.DownloadMusicArtistImages.Logo &&
                !ConfigurationManager.Configuration.DownloadMusicArtistImages.Primary)
            {
                return false;
            }

            return base.NeedsRefreshInternal(item, providerInfo);
        }

        protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

        /// <summary>
        /// Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="musicBrainzArtistId">The music brainz artist id.</param>
        /// <returns>System.String.</returns>
        internal static string GetArtistDataPath(IApplicationPaths appPaths, string musicBrainzArtistId)
        {
            var seriesDataPath = Path.Combine(GetArtistDataPath(appPaths), musicBrainzArtistId);

            if (!Directory.Exists(seriesDataPath))
            {
                Directory.CreateDirectory(seriesDataPath);
            }

            return seriesDataPath;
        }

        /// <summary>
        /// Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <returns>System.String.</returns>
        internal static string GetArtistDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.DataPath, "fanart-music");

            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            return dataPath;
        }
        
        /// <summary>
        /// Fetches metadata and returns true or false indicating if any work that requires persistence was done
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="force">if set to <c>true</c> [force].</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{System.Boolean}.</returns>
        public override async Task<bool> FetchAsync(BaseItem item, bool force, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //var artist = item;

            var musicBrainzId = item.GetProviderId(MetadataProviders.Musicbrainz);
            var url = string.Format(FanArtBaseUrl, ApiKey, musicBrainzId);

            var status = ProviderRefreshStatus.Success;

            var xmlPath = Path.Combine(GetArtistDataPath(ConfigurationManager.ApplicationPaths, musicBrainzId), "fanart.xml");
            
            using (var response = await HttpClient.Get(new HttpRequestOptions
            {
                Url = url,
                ResourcePool = FanArtResourcePool,
                CancellationToken = cancellationToken

            }).ConfigureAwait(false))
            {
                using (var xmlFileStream = new FileStream(xmlPath, FileMode.Create, FileAccess.Write, FileShare.Read, StreamDefaults.DefaultFileStreamBufferSize, FileOptions.Asynchronous))
                {
                    await response.CopyToAsync(xmlFileStream).ConfigureAwait(false);
                }
            }

            var doc = new XmlDocument();
            doc.Load(xmlPath);

            cancellationToken.ThrowIfCancellationRequested();

            if (doc.HasChildNodes)
            {
                string path;
                var hd = ConfigurationManager.Configuration.DownloadHDFanArt ? "hd" : "";
                if (ConfigurationManager.Configuration.DownloadMusicArtistImages.Logo && !item.HasImage(ImageType.Logo))
                {
                    var node =
                        doc.SelectSingleNode("//fanart/music/musiclogos/" + hd + "musiclogo/@url") ??
                        doc.SelectSingleNode("//fanart/music/musiclogos/musiclogo/@url");
                    path = node != null ? node.Value : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Debug("FanArtProvider getting ClearLogo for " + item.Name);
                        item.SetImage(ImageType.Logo, await _providerManager.DownloadAndSaveImage(item, path, LogoFile, SaveLocalMeta, FanArtResourcePool, cancellationToken).ConfigureAwait(false));
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (ConfigurationManager.Configuration.DownloadMusicArtistImages.Backdrops && item.BackdropImagePaths.Count == 0)
                {
                    var nodes = doc.SelectNodes("//fanart/music/artistbackgrounds//@url");
                    if (nodes != null)
                    {
                        var numBackdrops = 0;
                        item.BackdropImagePaths = new List<string>();
                        foreach (XmlNode node in nodes)
                        {
                            path = node.Value;
                            if (!string.IsNullOrEmpty(path))
                            {
                                Logger.Debug("FanArtProvider getting Backdrop for " + item.Name);
                                item.BackdropImagePaths.Add(await _providerManager.DownloadAndSaveImage(item, path, ("Backdrop" + (numBackdrops > 0 ? numBackdrops.ToString(UsCulture) : "") + ".jpg"), SaveLocalMeta, FanArtResourcePool, cancellationToken).ConfigureAwait(false));
                                numBackdrops++;
                                if (numBackdrops >= ConfigurationManager.Configuration.MaxBackdrops) break;
                            }
                        }

                    }

                }

                cancellationToken.ThrowIfCancellationRequested();

                if (ConfigurationManager.Configuration.DownloadMusicArtistImages.Art && !item.HasImage(ImageType.Art))
                {
                    var node =
                        doc.SelectSingleNode("//fanart/music/musicarts/" + hd + "musicart/@url") ??
                        doc.SelectSingleNode("//fanart/music/musicarts/musicart/@url");
                    path = node != null ? node.Value : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Debug("FanArtProvider getting ClearArt for " + item.Name);
                        item.SetImage(ImageType.Art, await _providerManager.DownloadAndSaveImage(item, path, ArtFile, SaveLocalMeta, FanArtResourcePool, cancellationToken).ConfigureAwait(false));
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (ConfigurationManager.Configuration.DownloadMusicArtistImages.Banner && !item.HasImage(ImageType.Banner))
                {
                    var node = doc.SelectSingleNode("//fanart/music/musicbanners/" + hd + "musicbanner/@url") ??
                               doc.SelectSingleNode("//fanart/music/musicbanners/musicbanner/@url");
                    path = node != null ? node.Value : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Debug("FanArtProvider getting Banner for " + item.Name);
                        item.SetImage(ImageType.Banner, await _providerManager.DownloadAndSaveImage(item, path, BannerFile, SaveLocalMeta, FanArtResourcePool, cancellationToken).ConfigureAwait(false));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Artist thumbs are actually primary images (they are square/portrait)
                if (ConfigurationManager.Configuration.DownloadMusicArtistImages.Primary && !item.HasImage(ImageType.Primary))
                {
                    var node = doc.SelectSingleNode("//fanart/music/artistthumbs/artistthumb/@url");
                    path = node != null ? node.Value : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Debug("FanArtProvider getting Primary image for " + item.Name);
                        item.SetImage(ImageType.Primary, await _providerManager.DownloadAndSaveImage(item, path, PrimaryFile, SaveLocalMeta, FanArtResourcePool, cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            SetLastRefreshed(item, DateTime.UtcNow, status);
            return true;
        }
    }
}
