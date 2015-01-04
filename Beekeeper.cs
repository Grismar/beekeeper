using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Configuration;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Net;
using System.Linq;

// external, from http://www.hybridgeotools.com/ some instructions at http://www.codeproject.com/Articles/20445/C-Customizable-Embedded-HTTPServer
using HybridDSP.Net.HTTP;

namespace MusicBeePlugin
{
    /* Beekeeper MusicBee Plugin.
     * Optionally, use the Beekeeper module containing types and methods for easy access to the Beekeeper MusicBee Plugin. (beekeeper.js)
     * The Beekeeper Javascript module also offers a bit more in the way of documenting the effect of all the API methods, so take a look, 
     * even if you're not going to be using it.
     *
     * This version was written for MusicBee 2.4.5404, API version 43.
     *
     * Future compatibility of the Beekeeper Plugin with MusicBee cannot be guaranteed and is at the mercy of the author of MusicBee.
     *
     * @version 1.0
     * @copyright Jaap van der Velde 2014
     * @license Apache-2.0
     *
     * Disclaimer: other than being written specifically for interaction with the MusicBee application, there exist no links between 
     * MusicBee and Beekeeper, nor between their makers. Use Beekeeper at your own risk. No guarantee is offered, no responsibility is 
     * taken for any damage or other adverse effects of use of Beekeeper, intended or otherwise.
     */

    /// <summary>
    /// Core class of the Beekeeper MusicBee plugin. Contains the web server and most added functionality outside the base Plugin class.
    /// </summary>
    /// <remarks>
    /// Beekeeper uses a technique called long polling to send events from MusicBee to a client. It uses JSON to encode data that is 
    /// exchanged (not XML) and it never sends JavaScript code as a payload. 
    /// For more on long polling see <see href="http://tools.ietf.org/html/draft-loreto-http-bidirectional-07">this article</see> or just
    /// Google around for available information on 'long polling', there's lots.
    /// </remarks>
    public class BeekeeperServer : IDisposable
    {
        /// <summary>
        /// A reference to the mbApiInterface to pass calls on to MusicBee and retrieve information.
        /// </summary>
        private static Plugin.MusicBeeApiInterface mbApiInterface;

        /// <summary>
        /// This is the server-side delay for long polling. After the give number of ms without new events occuring, the server will 
        /// send an empty array as a response.
        /// </summary>
        /// <remarks>Normally, the client should immediately follow-up an empty array response with a new request. 
        /// Note that any implemented timeout for a maximum wait on the client should be longer than longPollingDelay, if you want to 
        /// use it to determine if the BeekeeperServer is responding.</remarks>
        public const int longPollingDelay = 5000;
        /// <summary>
        /// This the path on the server for long polling calls.
        /// </summary>
        private const string longPollingPath = "/events";
        private const string picturePath = "/picture";
        private const string filePath = "/file";
        private const string statusPath = "/status";

        private bool _IsRunning;
        /// <summary>
        /// Read-only property which indicates whether the server is currently running.
        /// </summary>
        public bool IsRunning
        {
            get 
            {
                return this._IsRunning;
            }
        }

        private int _Port;
        /// <summary>
        /// Read-only property with port number the BeekeeperServer uses when it is running.
        /// </summary>
        public int Port
        {
            get 
            {
                return this._Port;
            }
        }

        private bool _Share;
        /// <summary>
        /// Indicates whether the BeekeeperServer will serve files from the persistent storage path of MusicBee.
        /// </summary>
        /// <remarks>The persistent storage path can be retrieved with a call to mbApiInterface.Setting_GetPersistentStoragePath()
        /// which is also exposed by BeekeeperServer as /GetPersistentStoragePath.</remarks>
        /// <seealso cref="GetPersistentStoragePath"/>
        public bool Share
        {
            get
            {
                return this._Share;
            }
            set
            {
                this._Share = value;
            }
        }

        private bool _ReadOnly;
        /// <summary>
        /// The ReadOnly property locks and unlocks methods that can modify the MusicBee database, or files.
        /// </summary>
        public bool ReadOnly
        {
            get
            {
                return this._ReadOnly;
            }
            set
            {
                this._ReadOnly = value;
            }
        }

        /// <summary>
        /// MusicBeeEvent instances are stored in the events Queues of sessions by calls to EnqueueEvent.
        /// </summary>
        /// <remarks>Parameters passed to the constructur will simply be serialized in HandleEventsRequest.</remarks>
        /// <seealso cref="_Sessions"/><seealso cref="EnqueueEvent"/><seealso cref="HandleEventsRequest"/>
        class MusicBeeEvent
        {
            private string _Name;
            /// <summary>
            /// The read-only Name property contains the name of the event.
            /// </summary>
            public string Name
            {
                get
                {
                    return this._Name;
                }
            }
            /// <summary>
            /// The read-only Name property contains any parameters of the event.
            /// </summary>
            private object[] _Parameters;
            public object[] Parameters
            {
                get 
                {
                    return this._Parameters;
                }
            }

            /// <summary>
            /// Constructor of the MusicBeeEvent, setting Name and Parameters.
            /// </summary>
            public MusicBeeEvent(string name, object[] parameters)
            {
                this._Name = name;
                this._Parameters = parameters;
            }
        }

        /// <summary>
        /// Contains the current sessions, indexed by their session id.
        /// </summary>
        private Dictionary<string, Session> _Sessions;
        /// <summary>
        /// Class used for collecting events per session and for waiting on long polling results.
        /// </summary>
        class Session
        {
            /// <summary>
            /// Any notifications from MusicBee are collected on this queue.
            /// </summary>
            /// <remarks>
            /// The queues are flushed by HandleEventsRequest in response to an longPollingPath call.
            /// When a call was already waiting, a queue is flushed immediately with just one event.
            /// </remarks>
            public Queue<MusicBeeEvent> events = new Queue<MusicBeeEvent>();
            /// <summary>
            /// The eventsRequestMRE is used to wait for calls from a client.
            /// </summary>
            public ManualResetEvent eventsRequestMRE;
        }

        /// <summary>
        /// Stores temporary url's for images that are made available to the web client by NowPlaying_GetArtistPictureUrl
        /// </summary>
        private HashSet<string> tempUrls;

        /// <summary>
        /// The event type for handling requests for events.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        public delegate void EventsRequest(HTTPServerResponse response);
        /// <summary>
        /// The main handler for handling EventsRequest events.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        public void HandleEventsRequest(HTTPServerResponse response)
        {
            // get session for this response
            Session session = _Sessions[response.SessionID];
            // if no events are already queued
            if (session.events.Count == 0)
            {
                // start waiting
                session.eventsRequestMRE = new ManualResetEvent(false);
                session.eventsRequestMRE.WaitOne(longPollingDelay);
            }
            // otherwise (some events queued or after a timeout on the wait)
            response.ContentType = "application/json";
            using (Stream s = response.Send())
            using (TextWriter tw = new StreamWriter(s))
            {
                var jss = new JavaScriptSerializer();
                // serialize events for session and write them to the stream
                tw.WriteLine(jss.Serialize(session.events));
                session.events.Clear();
            }
        }

        /// <summary>
        /// The event type for handling requests for a picture generated by NowPlaying_GetArtistPicture and served by NowPlaying_GetArtistPictureUrl.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="tempPath">The local path for the temporary file to serve, as cleared previously (should be in tempUrls).</param>
        public delegate void TempRequest(HTTPServerResponse response, string tempPath);
        /// <summary>
        /// The main handler for handling TempRequest events.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="tempPath">The local path for the temporary file to serve, as cleared previously (should be in tempUrls).</param>
        public void HandleTempRequest(HTTPServerResponse response, string tempPath)
        {
            using (Stream s = response.Send())
                if (tempUrls.Contains(tempPath))
                {
                    try
                    {

                        using (FileStream fs = new FileStream(HttpUtility.UrlDecode(tempPath), FileMode.Open))
                            fs.CopyTo(s);
                    }
                    catch
                    {
                        using (TextWriter tw = new StreamWriter(s))
                            tw.Write("Beekeeper MusicBee plugin tried to read file from\n" + HttpUtility.UrlDecode(tempPath) + "\nbut failed.");
                    }
                }
                else
                {
                    using (TextWriter tw = new StreamWriter(s))
                        tw.Write("Beekeeper MusicBee plugin tried to read file from\n" + HttpUtility.UrlDecode(tempPath) + "\nbut this path wasn't cleared by Beekeeper.");
                }
        }

        /// <summary>
        /// The event type for handling requests for a file.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="subPath">The relative path from the persistent storage of MusicBee to the file to serve.</param>
        public delegate void FileRequest(HTTPServerResponse response, string subPath);
        /// <summary>
        /// The main handler for handling FileRequest events.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="subPath">The relative path from the persistent storage of MusicBee to the file to serve.</param>
        /// <remarks>Note that the persistent storage path of MusicBee is relative to the user currently logged in and using MusicBee.
        /// If you require that the served files are stored in a central, shared location, you can add a shortcut to that folder in 
        /// each of the MusicBee users' persistent storage folders. Make sure to use the same name in each instance.</remarks>
        public void HandleFileRequest(HTTPServerResponse response, string subPath)
        {
            if (Share)
            {
                subPath = subPath.Replace('/', '\\');
                // serve a file
                string extension = Path.GetExtension(subPath).ToLower();
                // set content type for a few typical file formats
                switch (extension)
                {
                    case ".htm":
                    case ".html":
                        response.ContentType = "text/html";
                        break;
                    case ".css":
                        response.ContentType = "text/css";
                        break;
                    case ".js":
                        response.ContentType = "application/x-javascript";
                        break;
                    // default untouched
                }
                ;
                using (Stream s = response.Send())
                    try
                    {
                        using (FileStream fs = new FileStream(mbApiInterface.Setting_GetPersistentStoragePath() + "files\\" + subPath, FileMode.Open))
                            fs.CopyTo(s);
                    }
                    catch
                    {
                        using (TextWriter tw = new StreamWriter(s))
                            tw.Write("Beekeeper MusicBee plugin tried to read file\n  " + subPath + "\nfrom\n  " + mbApiInterface.Setting_GetPersistentStoragePath() + "files\\\nbut failed.");
                    }
            } 
            else 
            {
                // indicate that it's not allowed to serve a file
                response.StatusAndReason = HTTPServerResponse.HTTPStatus.HTTP_FORBIDDEN;
                using (Stream s = response.Send())
                using (TextWriter tw = new StreamWriter(s))
                    tw.Write("Beekeeper MusicBee plugin is configured not to serve files.");
            }
        }

        public delegate void RegisterSession(HTTPServerRequest request, HTTPServerResponse response);
        /// <summary>
        /// Registers the current session with _Sessions and sets the session id on the response cookie for long polling calls.
        /// </summary>
        /// <param name="request">The current HTTPServerRequest.</param>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        public void HandleRegisterSession(HTTPServerRequest request, HTTPServerResponse response)
        {
            if (!_Sessions.ContainsKey(request.SessionID))
            {
                _Sessions.Add(request.SessionID, new Session());
            }
            response.SetSessionID(longPollingPath, request.SessionID);
        }

        public delegate void StatusRequest(HTTPServerResponse response);
        /// <summary>
        /// This type of record is created for every call to HandleStatusRequest.
        /// </summary>
        /// <seealso cref="HandeStatusRequest"/>
        class BeekeeperStatusRecord: IDisposable
        {
            /// <summary>
            /// Whether the server is configured to serve files. Maps to this._Share.
            /// </summary>
            public bool servingFiles;
            /// <summary>
            /// For how many clients the server is currently tracking events. Maps to this._Sessions.Count.
            /// </summary>
            public int numberOfClients;
            /// <summary>
            /// List of current session id's with the number of events currently queued up (and unread) for that session.
            /// </summary>
            public Dictionary<string, int> sessions;

            public void Dispose() { }
        }
        /// <summary>
        /// Handles requests for a status record of type BeekeeperStatusRecord.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <seealso cref="BeekeeperStatusRecord"/>
        public void HandleStatusRequest(HTTPServerResponse response)
        {
            using (BeekeeperStatusRecord bsr = new BeekeeperStatusRecord())
            {
                bsr.servingFiles = _Share;
                bsr.numberOfClients = _Sessions.Count;
                bsr.sessions = new Dictionary<string, int>();
                foreach (KeyValuePair<string, Session> kvp in _Sessions)
                {
                    bsr.sessions.Add(kvp.Key, kvp.Value.events.Count);
                }

                response.ContentType = "application/json";
                using (Stream s = response.Send())
                using (TextWriter tw = new StreamWriter(s))
                {
                    var jss = new JavaScriptSerializer();
                    tw.WriteLine(jss.Serialize(bsr));
                }
            }
        }

        class SyncDelta
        {
            public string[] newFiles;
            public string[] updatedFiles;
            public string[] deletedFiles;
        }

        public Plugin.PluginInfo about = new Plugin.PluginInfo();

        public string UrlToLink(string url)
        {
            if (url.ToLower().StartsWith("http://"))
            {
                // already a link (i.e. url suitable for Web API)
                return url;
            }
            else
            {
                // url encode filename local to MusicBee and save for later reference
                url = HttpUtility.UrlEncode(url);
                tempUrls.Add(url);
                // pass usable url for web client
                return "/picture/" + url;
            }
        }

        /// <summary>
        /// The event type for handling requests to call the API.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="name">The name of the method to call (as it's exposed by Beekeeper).</param>
        /// <param name="parameters">The parameters, if any, to pass to the called method.</param>
        public delegate bool CallRequest(HTTPServerResponse response, string name, Dictionary<string, object> parameters);
        /// <summary>
        /// The main handler for handling CallRequest events. Selects the right method on the Plugin API, performs type conversion 
        /// and provides some double implementations working around some naming issues. Also provides a number of alternative 
        /// methods suitable for use on a web API.
        /// </summary>
        /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
        /// <param name="name">The name of the method to call (as it's exposed by Beekeeper).</param>
        /// <param name="parameters">The parameters, if any, to pass to the called method.</param>
        public bool HandleCallRequest(HTTPServerResponse response, string name, Dictionary<string, object> parameters)
        {
            // result to send back on response
            object result = null;
            // temporary variables to prepare for specific API calls
            string sourceFileUrl;
            string value;
            string query;
            string playlistUrl;
            string id;
            string defaultText;
            string folderName;
            string playlistName;
            string filename;
            string artistName;
            string keyTags;
            string valueTags;
            string key;
            string errorMessage;
            string url;
            string targetFolder;
            string parameterName;
            string[] filenames;
            string[] sourceFileUrls;
            string[] urls;
            string[] results;
            string[] files;
            string[] ids;
            string[] values;
            string[] cachedFiles;
            int index;
            int position;
            int count;
            int offset;
            int fadingPercent;
            int fadingColor;
            int width;
            int height;
            int toIndex;
            int[] fromIndices;
            float volume;
            double minimumArtistSimilarityRating;
            bool mute;
            bool shuffle;
            bool enabled;
            bool crossfade;
            bool localOnly;
            bool success;
            bool cancelDownload;
            DateTime updatedSince;
            SyncDelta syncDelta;
            List<string> playlist;
            List<string> copiedItems;
            SortedSet<int> uniqueIndices;
            Plugin.FilePropertyType fptype;
            Plugin.MetaDataType field;
            Plugin.MetaDataType[] fields;
            Plugin.SkinElement element;
            Plugin.ElementState state;
            Plugin.ElementComponent component;
            Plugin.LyricsType ltype;
            Plugin.RepeatMode repeat;
            Plugin.ReplayGainMode replayGain;
            Plugin.PlayButtonType button;
            Plugin.DeviceIdType idType;
            Plugin.SettingId settingId;
            Plugin.LibraryCategory category;
            Plugin.DownloadTarget target;
            // copy parameters to a case-insensitive comparer to deal with variation in parameter naming
            // only used in cases where the naming isn't consistent with the overall naming scheme
            Dictionary<string, object> ciParameters = new Dictionary<string, object>(parameters, StringComparer.CurrentCultureIgnoreCase);
            // switch statement dealing with all calls
            try
            {
                switch (name)
                {
                    case "Setting_GetPersistentStoragePath": // string ()
                        result = mbApiInterface.Setting_GetPersistentStoragePath();
                        break;
                    // not canon, retrieves Beekeeper specific setting
                    case "Setting_GetReadOnly_BK": // boolean ()
                        result = ReadOnly;
                        break;
                    // not canon, retrieves Beekeeper specific setting
                    case "Setting_GetShare_BK": // boolean ()
                        result = Share;
                        break;
                    // not canon, retrieves Beekeeper specific setting
                    case "Setting_GetVersion_BK": // int[] ()
                        result = new int[3] { about.VersionMajor, about.VersionMinor, about.Revision };
                        break;
                    case "Setting_GetSkin": // string ()
                        result = mbApiInterface.Setting_GetSkin();
                        break;
                    // not canon, but fits American English spelling
                    case "Setting_GetSkinElementColor": // int (SkinElement element, ElementState state, ElementComponent component);
                    // canon, fits British English spelling
                    case "Setting_GetSkinElementColour": // int (SkinElement element, ElementState state, ElementComponent component);
                        element = (Plugin.SkinElement)parameters["element"];
                        state = (Plugin.ElementState)parameters["element"];
                        component = (Plugin.ElementComponent)parameters["element"];
                        result = mbApiInterface.Setting_GetSkinElementColour(element, state, component);
                        break;
                    case "Setting_IsWindowBordersSkinned": // bool ()
                        result = mbApiInterface.Setting_IsWindowBordersSkinned();
                        break;
                    case "Library_GetFileProperty": // string (string sourceFileUrl, FilePropertyType type) 
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        fptype = (Plugin.FilePropertyType)parameters["type"];
                        result = mbApiInterface.Library_GetFileProperty(sourceFileUrl, fptype);
                        break;
                    case "Library_GetFileTag": // string (string sourceFileUrl, MetaDataType field)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.Library_GetFileTag(sourceFileUrl, field);
                        break;
                    case "Library_SetFileTag": // bool (string sourceFileUrl, MetaDataType field, string value)
                        if (!ReadOnly)
                        {
                            sourceFileUrl = (string)parameters["sourceFileUrl"];
                            field = (Plugin.MetaDataType)parameters["field"];
                            value = (string)ciParameters["value"];
                            result = mbApiInterface.Library_SetFileTag(sourceFileUrl, field, value);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
                    case "Library_CommitTagsToFile": // bool (string sourceFileUrl)
                        if (!ReadOnly)
                        {
                            sourceFileUrl = (string)parameters["sourceFileUrl"];
                            result = mbApiInterface.Library_CommitTagsToFile(sourceFileUrl);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
                    case "Library_GetLyrics": // string (string sourceFileUrl, LyricsType type)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        ltype = (Plugin.LyricsType)parameters["type"];
                        result = mbApiInterface.Library_GetLyrics(sourceFileUrl, ltype);
                        break;
                    case "Library_GetArtwork": // string (string sourceFileUrl, int index)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        index = (int)parameters["index"];
                        result = mbApiInterface.Library_GetArtwork(sourceFileUrl, index);
                        break;
                    case "Library_QueryFiles": // bool (string query)
                        query = (string)parameters["query"];
                        result = mbApiInterface.Library_QueryFiles(query);
                        break;
                    case "Library_QueryGetNextFile": // string ()
                        result = mbApiInterface.Library_QueryGetNextFile();
                        break;
                    case "Player_GetPosition": // int ()
                        result = mbApiInterface.Player_GetPosition();
                        break;
                    case "Player_SetPosition": // bool (int position)
                        position = (int)parameters["position"];
                        result = mbApiInterface.Player_SetPosition(position);
                        break;
                    case "Player_GetPlayState": // PlayState ()
                        result = mbApiInterface.Player_GetPlayState();
                        break;
                    case "Player_PlayPause": // bool ()
                        result = mbApiInterface.Player_PlayPause();
                        break;
                    case "Player_Stop": // bool ()
                        result = mbApiInterface.Player_Stop();
                        break;
                    case "Player_StopAfterCurrent": // bool ()
                        result = mbApiInterface.Player_StopAfterCurrent();
                        break;
                    case "Player_PlayPreviousTrack": // bool ()
                        result = mbApiInterface.Player_PlayPreviousTrack();
                        break;
                    case "Player_PlayNextTrack": // bool ()
                        result = mbApiInterface.Player_PlayNextTrack();
                        break;
                    case "Player_StartAutoDj": // bool ()
                        result = mbApiInterface.Player_StartAutoDj();
                        break;
                    case "Player_EndAutoDj": // bool ()
                        result = mbApiInterface.Player_EndAutoDj();
                        break;
                    case "Player_GetVolume": // float ()
                        result = mbApiInterface.Player_GetVolume();
                        break;
                    case "Player_SetVolume": // bool (float volume)
                        volume = Convert.ToSingle(parameters["volume"]);
                        result = mbApiInterface.Player_SetVolume(volume);
                        break;
                    case "Player_GetMute": // bool ()
                        result = mbApiInterface.Player_GetMute();
                        break;
                    case "Player_SetMute": // bool (bool mute)
                        mute = (bool)parameters["mute"];
                        result = mbApiInterface.Player_SetMute(mute);
                        break;
                    case "Player_GetShuffle": // bool ()
                        result = mbApiInterface.Player_GetShuffle();
                        break;
                    case "Player_SetShuffle": // bool (bool shuffle)
                        shuffle = (bool)parameters["shuffle"];
                        result = mbApiInterface.Player_SetShuffle(shuffle);
                        break;
                    case "Player_GetRepeat": // RepeatMode ()
                        result = mbApiInterface.Player_GetRepeat();
                        break;
                    case "Player_SetRepeat": // bool (RepeatMode repeat)
                        repeat = (Plugin.RepeatMode)parameters["repeat"];
                        result = mbApiInterface.Player_SetRepeat(repeat);
                        break;
                    // not canon, but fits American English spelling
                    case "Player_GetEqualizerEnabled": // bool ()
                    // canon, fits British English spelling
                    case "Player_GetEqualiserEnabled": // bool ()
                        result = mbApiInterface.Player_GetEqualiserEnabled();
                        break;
                    // not canon, but fits American English spelling
                    case "Player_SetEqualizerEnabled": // bool (bool enabled)
                    // canon, fits British English spelling
                    case "Player_SetEqualiserEnabled": // bool (bool enabled)
                        enabled = (bool)parameters["enabled"];
                        result = mbApiInterface.Player_SetEqualiserEnabled(enabled);
                        break;
                    case "Player_GetDspEnabled": // bool ()
                        result = mbApiInterface.Player_GetDspEnabled();
                        break;
                    case "Player_SetDspEnabled": // bool (bool enabled)
                        enabled = (bool)parameters["enabled"];
                        result = mbApiInterface.Player_SetDspEnabled(enabled);
                        break;
                    case "Player_GetScrobbleEnabled": // bool ()
                        result = mbApiInterface.Player_GetScrobbleEnabled();
                        break;
                    case "Player_SetScrobbleEnabled": // bool (bool enabled)
                        enabled = (bool)parameters["enabled"];
                        result = mbApiInterface.Player_SetScrobbleEnabled(enabled);
                        break;
                    case "NowPlaying_GetFileUrl": // string ()
                        result = mbApiInterface.NowPlaying_GetFileUrl();
                        break;
                    case "NowPlaying_GetDuration": // int ()
                        result = mbApiInterface.NowPlaying_GetDuration();
                        break;
                    case "NowPlaying_GetFileProperty": // string (FilePropertyType type)
                        fptype = (Plugin.FilePropertyType)parameters["type"];
                        result = mbApiInterface.NowPlaying_GetFileProperty(fptype);
                        break;
                    case "NowPlaying_GetFileTag": // string (MetaDataType field)
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.NowPlaying_GetFileTag(field);
                        break;
                    case "NowPlaying_GetLyrics": // string ()
                        result = mbApiInterface.NowPlaying_GetLyrics();
                        break;
                    case "NowPlaying_GetArtwork": // string ()
                        result = mbApiInterface.NowPlaying_GetArtwork();
                        break;
                    case "NowPlayingList_Clear": // bool ()
                        result = mbApiInterface.NowPlayingList_Clear();
                        break;
                    case "NowPlayingList_QueryFiles": // bool (string query)
                        query = (string)parameters["query"];
                        result = mbApiInterface.NowPlayingList_QueryFiles(query);
                        break;
                    case "NowPlayingList_QueryGetNextFile": // string ()
                        result = mbApiInterface.NowPlayingList_QueryGetNextFile();
                        break;
                    case "NowPlayingList_PlayNow": // bool (string sourceFileUrl)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        result = mbApiInterface.NowPlayingList_PlayNow(sourceFileUrl);
                        break;
                    case "NowPlayingList_QueueNext": // bool (string sourceFileUrl)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        result = mbApiInterface.NowPlayingList_QueueNext(sourceFileUrl);
                        break;
                    case "NowPlayingList_QueueLast": // bool (string sourceFileUrl)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        result = mbApiInterface.NowPlayingList_QueueLast(sourceFileUrl);
                        break;
                    case "NowPlayingList_PlayLibraryShuffled": // bool ()
                        result = mbApiInterface.NowPlayingList_PlayLibraryShuffled();
                        break;
                    case "Playlist_QueryPlaylists": // bool ()
                        result = mbApiInterface.Playlist_QueryPlaylists();
                        break;
                    case "Playlist_QueryGetNextPlaylist": // string ()
                        result = mbApiInterface.Playlist_QueryGetNextPlaylist();
                        break;
            // not canon, follows normal naming scheme
                    case "Playlist_GetFormat": // PlaylistFormat (string playlistUrl)
            // canon, deviates from normal naming scheme
                    case "Playlist_GetType": // PlaylistFormat (string playlistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        result = mbApiInterface.Playlist_GetType(playlistUrl);
                        break;
                    case "Playlist_QueryFiles": // bool (string PlaylistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        result = mbApiInterface.Playlist_QueryFiles(playlistUrl);
                        break;
                    case "Playlist_QueryGetNextFile": // string ()
                        result = mbApiInterface.Playlist_QueryGetNextFile();
                        break;
                    case "MB_GetWindowHandle": // void ()
                        throw new Exception("MB_GetWindowHandle is meaningless or impractical in web context. No replacement available.");
                    case "MB_RefreshPanels": // boolean ()
                        mbApiInterface.MB_RefreshPanels();
                        result = true;
                        break;
                    case "MB_SendNotification": // ()
                        throw new Exception("MB_SendNotification is meaningless or impractical in web context. (Notifications not [yet] implemented as part of events)");
                    case "MB_AddMenuItem": // ()
                        throw new Exception("MB_AddMenuItem is meaningless or impractical in web context. No replacement available.");
                    case "Setting_GetFieldName": // string (MetaDataType field)
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.Setting_GetFieldName(field);
                        break;
            // [Obsolete("Use Library_QueryFilesEx")]
                    case "Library_QueryGetAllFiles": // ()
                        throw new Exception("Library_QueryGetAllFiles is obsolete. Use Library_QueryFilesEx.");
            // [Obsolete("Use NowPlayingList_QueryFilesEx")]
                    case "NowPlayingList_QueryGetAllFiles": // ()
                        throw new Exception("NowPlayingList_QueryGetAllFiles is obsolete. Use Playlist_QueryFilesEx.");
            // [Obsolete("Use Playlist_QueryFilesEx")]
                    case "Playlist_QueryGetAllFiles": // ()
                        throw new Exception("Playlist_QueryGetAllFiles is obsolete. Use Playlist_QueryFilesEx.");
                    case "MB_CreateBackgroundTask": // ()
                        throw new Exception("MB_CreateBackgroundTask is meaningless or impractical in web context. No replacement available.");
                    case "MB_SetBackgroundTaskMessage": // ()
                        throw new Exception("MB_SetBackgroundTaskMessage is meaningless or impractical in web context. No replacement available.");
                    case "MB_RegisterCommand": // ()
                        throw new Exception("MB_RegisterCommand is meaningless or impractical in web context. No replacement available.");
                    case "Setting_GetDefaultFont": // ()
                        throw new Exception("Setting_GetDefaultFont is meaningless or impractical in web context. Use Setting_GetDefaultFontName_BK to retrieve font name.");
            // Unique to Beekeeper, replaces Setting_GetDefaultFont for web API
                    case "Setting_GetDefaultFontName_BK": // string ()
                        result = mbApiInterface.Setting_GetDefaultFont().Name;
                        break;
                    case "Player_GetShowTimeRemaining": // bool ()
                        result = mbApiInterface.Player_GetShowTimeRemaining();
                        break;
                    case "NowPlayingList_GetCurrentIndex": // int ()
                        result = mbApiInterface.NowPlayingList_GetCurrentIndex();
                        break;
                    // not canon, follows normal naming scheme
                    case "NowPlayingList_GetFileUrlAt":
                    // not canon, deviates from normal naming scheme, but matches other MusicBee API methods
                    case "NowPlayingList_GetListFileUrlAt":
                    // not canon, deviates from normal naming scheme, but matches other MusicBee API methods
                    case "NowPlayingList_GetFileUrl":
                    // canon, deviates from normal naming scheme
                    case "NowPlayingList_GetListFileUrl": // string (int index)
                        index = (int)parameters["index"];
                        result = mbApiInterface.NowPlayingList_GetListFileUrl(index);
                        break;
                    // not canon, follows normal naming scheme
                    case "NowPlayingList_GetFilePropertyAt":
                    // canon, deviates from normal naming scheme
                    case "NowPlayingList_GetFileProperty": // string (int index, FilePropertyType type)
                        index = (int)parameters["index"];
                        fptype = (Plugin.FilePropertyType)parameters["type"];
                        result = mbApiInterface.NowPlayingList_GetFileProperty(index, fptype);
                        break;
                    // not canon, follows normal naming scheme
                    case "NowPlayingList_GetFileTagAt":
                    // canon, deviates from normal naming scheme
                    case "NowPlayingList_GetFileTag": // string (int index, MetaDataType field)
                        index = (int)parameters["index"];
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.NowPlayingList_GetFileTag(index, field);
                        break;
                    case "NowPlaying_GetSpectrumData": // ()
                        throw new Exception("NowPlaying_GetSpectrumData is meaningless or impractical in web context. No replacement available.");
                    case "NowPlaying_GetSoundGraph": // ()
                        throw new Exception("NowPlaying_GetSoundGraph is meaningless or impractical in web context. No replacement available.");
                    case "MB_GetPanelBounds": // ()
                        throw new Exception("MB_GetPanelBounds is meaningless or impractical in web context. No replacement available.");
                    case "MB_AddPanel": // ()
                        throw new Exception("MB_AddPanel is meaningless or impractical in web context. No replacement available.");
                    case "MB_RemovePanel": // ()
                        throw new Exception("MB_RemovePanel is meaningless or impractical in web context. No replacement available.");
                    // not canon, but fits American English spelling
                    case "MB_GetLocalization": // string (string id, string defaultText)
                    // canon, fits British English spelling
                    case "MB_GetLocalisation": // string (string id, string defaultText)
                        id = (string)parameters["id"];
                        defaultText = (string)parameters["defaultText"];
                        result = mbApiInterface.MB_GetLocalisation(id, defaultText);
                        break;
                    case "NowPlayingList_IsAnyPriorTracks": // bool ()
                        result = mbApiInterface.NowPlayingList_IsAnyPriorTracks();
                        break;
                    case "NowPlayingList_IsAnyFollowingTracks": // bool ()
                        result = mbApiInterface.NowPlayingList_IsAnyFollowingTracks();
                        break;
                    // not canon, but fits American English spelling
                    case "Player_ShowEqualizer": // bool ()
                    // canon, fits British English spelling
                    case "Player_ShowEqualiser": // bool ()
                        throw new Exception("Player_ShowEqualiser is defective in web context, may crash MusicBee and has been disabled.");
                    case "Player_GetAutoDjEnabled": // bool ()
                        result = mbApiInterface.Player_GetAutoDjEnabled();
                        break;
                    case "Player_GetStopAfterCurrentEnabled": // bool ()
                        result = mbApiInterface.Player_GetStopAfterCurrentEnabled();
                        break;
                    case "Player_GetCrossfade": // bool ()
                        result = mbApiInterface.Player_GetCrossfade();
                        break;
                    case "Player_SetCrossfade": // bool (bool crossfade)
                        crossfade = (bool)parameters["crossfade"];
                        result = mbApiInterface.Player_SetCrossfade(crossfade);
                        break;
                    case "Player_GetReplayGainMode": // ReplayGainMode ()
                        result = mbApiInterface.Player_GetReplayGainMode();
                        break;
                    case "Player_SetReplayGainMode": // bool (ReplayGainMode mode/replayGain)
                        if (parameters.ContainsKey("mode")) 
                            // canon, deviates from normal naming scheme
                            parameterName = "mode"; 
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "replayGain"; 
                        replayGain = (Plugin.ReplayGainMode)parameters[parameterName];
                        result = mbApiInterface.Player_SetReplayGainMode(replayGain);
                        break;
                    case "Player_QueueRandomTracks": // int (int count)
                        count = (int)parameters["count"];
                        result = mbApiInterface.Player_QueueRandomTracks(count);
                        break;
                    case "Setting_GetDataType": // DataType (MetaDataType field)
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.Setting_GetDataType(field);
                        break;
                    case "NowPlayingList_GetNextIndex": // int (int offset)
                        offset = (int)parameters["offset"];
                        result = mbApiInterface.NowPlayingList_GetNextIndex(offset);
                        break;
                    case "NowPlaying_GetArtistPicture": // string (int fadingPercent)
                        throw new Exception("NowPlaying_GetArtistPicture is meaningless or impractical in web context. Use NowPlaying_GetArtistPictureLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces NowPlaying_GetArtistPicture for web API
                    case "NowPlaying_GetArtistPictureLink_BK": // string (int fadingPercent)
                        fadingPercent = (int)parameters["fadingPercent"];
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.NowPlaying_GetArtistPicture(fadingPercent);
                        result = UrlToLink(filename);
                        break;
                    case "NowPlaying_GetDownloadedArtwork": // string ()
                        throw new Exception("NowPlaying_GetDownloadedArtwork is meaningless or impractical in web context. Use NowPlaying_GetDownloadedArtworkLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces NowPlaying_GetDownloadedArtwork for web API
                    case "NowPlaying_GetDownloadedArtworkLink_BK": // string ()
                        filename = mbApiInterface.NowPlaying_GetDownloadedArtwork();
                        result = UrlToLink(filename);
                        break;
            // api version 16
                    case "MB_ShowNowPlayingAssistant": // bool ()
                        throw new Exception("MB_ShowNowPlayingAssistant is defective in web context, may crash MusicBee and has been disabled.");
            // api version 17
                    case "NowPlaying_GetDownloadedLyrics": // string ()
                        result = mbApiInterface.NowPlaying_GetDownloadedLyrics();
                        break;
            // api version 18
                    case "Player_GetShowRatingTrack": // bool ()
                        result = mbApiInterface.Player_GetShowRatingTrack();
                        break;
                    case "Player_GetShowRatingLove": // bool ()
                        result = mbApiInterface.Player_GetShowRatingLove();
                        break;
            // api version 19
                    case "MB_CreateParameterisedBackgroundTask": // ()
                        throw new Exception("MB_CreateParameterisedBackgroundTask is meaningless or impractical in web context. No replacement available.");
                    case "Setting_GetLastFmUserId": // string ()
                        result = mbApiInterface.Setting_GetLastFmUserId();
                        break;
                    case "Playlist_GetName": // string (string playlistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        result = mbApiInterface.Playlist_GetName(playlistUrl);
                        break;
                    case "Playlist_CreatePlaylist": // string (string folderName, string playlistName, string[] filenames)
                        if (!ReadOnly)
                        {
                            folderName = (string)parameters["folderName"];
                            playlistName = (string)parameters["playlistName"];
                            // cast the arraylist explictly to string and convert to an array
                            filenames = ((System.Collections.ArrayList)parameters["filenames"]).Cast<string>().ToArray();
                            result = mbApiInterface.Playlist_CreatePlaylist(folderName, playlistName, filenames);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
                    case "Playlist_SetFiles": // bool (string playlistUrl, filenames)
                        if (!ReadOnly)
                        {
                            playlistUrl = (string)parameters["playlistUrl"];
                            // cast the arraylist explictly to string and convert to an array
                            filenames = ((System.Collections.ArrayList)parameters["filenames"]).Cast<string>().ToArray();
                            result = mbApiInterface.Playlist_SetFiles(playlistUrl, filenames);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
                    case "Library_QuerySimilarArtists": // string (string artistName, double minimumArtistSimilarityRating)
                        artistName = (string)parameters["artistName"];
                        minimumArtistSimilarityRating = Convert.ToDouble(parameters["minimumArtistSimilarityRating"]);
                        result = mbApiInterface.Library_QuerySimilarArtists(artistName, minimumArtistSimilarityRating);
                        break;
                    case "Library_QueryLookupTable": // bool (string keyTags, string valueTags, string query)
                        keyTags = (string)parameters["keyTags"];
                        valueTags = (string)parameters["valueTags"];
                        query = (string)parameters["query"];
                        result = mbApiInterface.Library_QueryLookupTable(keyTags, valueTags, query);
                        break;
                    case "Library_QueryGetLookupTableValue": // string (string key)
                        key = (string)parameters["key"];
                        result = mbApiInterface.Library_QueryGetLookupTableValue(key);
                        break;
                    case "NowPlayingList_QueueFilesNext": // bool (string[] sourcefileUrl[s]/sourcefileUrl[s])
                        // not canon, avoid sourcefileUrl/sourceFileUrl and sourcefileUrls/sourceFileUrls conflict
                        if (ciParameters.ContainsKey("sourcefileUrl"))
                            // canon, (ALSO) deviates from normal naming scheme
                            parameterName = "sourcefileUrl";
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "sourcefileUrls";
                        // cast the arraylist explictly to string and convert to an array
                        sourceFileUrls = ((System.Collections.ArrayList)ciParameters["sourceFileUrls"]).Cast<string>().ToArray();
                        result = mbApiInterface.NowPlayingList_QueueFilesNext(sourceFileUrls);
                        break;
                    case "NowPlayingList_QueueFilesLast": // bool (string[] sourcefileUrl[s]/sourcefileUrl[s])
                        // not canon, avoid sourcefileUrl/sourceFileUrl and sourcefileUrls/sourceFileUrls conflict
                        if (ciParameters.ContainsKey("sourcefileUrl"))
                            // canon, (ALSO) deviates from normal naming scheme
                            parameterName = "sourcefileUrl";
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "sourcefileUrls";
                        // cast the arraylist explictly to string and convert to an array
                        sourceFileUrls = ((System.Collections.ArrayList)ciParameters["sourceFileUrls"]).Cast<string>().ToArray();
                        result = mbApiInterface.NowPlayingList_QueueFilesLast(sourceFileUrls);
                        break;
            // api version 20
                    case "Setting_GetWebProxy": // string ()
                        result = mbApiInterface.Setting_GetWebProxy();
                        break;
            // api version 21
                    // not canon, but fits atypical naming scheme of several other MusicBee Plugin API methods
                    case "NowPlayingList_Remove": // bool (int index)
                    // canon, fits normal naming scheme 
                    case "NowPlayingList_RemoveAt": // bool (int index)
                        index = (int)parameters["index"];
                        result = mbApiInterface.NowPlayingList_RemoveAt(index);
                        break;
            // api version 22
                    // not canon, but fits atypical naming scheme of several other MusicBee Plugin API methods
                    case "Playlist_Remove": // bool (int index)
                    // canon, fits normal naming scheme 
                    case "Playlist_RemoveAt": // bool (string playlistUrl, int index)
                        if (!ReadOnly)
                        {
                            playlistUrl = (string)parameters["playlistUrl"];
                            index = (int)parameters["index"];
                            result = mbApiInterface.Playlist_RemoveAt(playlistUrl, index);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
            // api version 23
                    case "MB_SetPanelScrollableArea": // ()
                        throw new Exception("MB_SetPanelScrollableArea is meaningless or impractical in web context. No replacement available.");
            // api version 24
                    case "MB_InvokeCommand": // ()
                        throw new Exception("MB_InvokeCommand is meaningless or impractical in web context. No replacement available.");
                    case "MB_OpenFilterInTab": // ()
                        throw new Exception("MB_OpenFilterInTab is meaningless or impractical in web context. No replacement available.");
            // api version 25
                    case "MB_SetWindowSize": // bool (int width, int height)
                        width = (int)parameters["width"];
                        height = (int)parameters["height"];
                        result = mbApiInterface.MB_SetWindowSize(width, height);
                        break;
                    case "Library_GetArtistPicture": // string (string artistName, int fadingPercent, int fadingColor/fadingColour)
                        throw new Exception("Library_GetArtistPicture is meaningless or impractical in web context. Use Library_GetArtistPictureLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces Library_GetArtistPicture for web API
                    case "Library_GetArtistPictureLink_BK": // string (string artistName, int fadingPercent, int fadingColor/fadingColour)
                        artistName = (string)parameters["artistName"];
                        fadingPercent = (int)parameters["fadingPercent"];
                        if (parameters.ContainsKey("fadingColour"))
                            // canon, deviates from normal naming scheme
                            parameterName = "fadingColour";
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "fadingColor";
                        fadingColor = (int)parameters[parameterName];

                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.Library_GetArtistPicture(artistName, fadingPercent, fadingColor);
                        result = UrlToLink(filename);
                        break;
                    case "Pending_GetFileUrl": // string ()
                        result = mbApiInterface.Pending_GetFileUrl();
                        break;
                    case "Pending_GetFileProperty": // string (FilePropertyType field/type)
                        if (parameters.ContainsKey("type"))
                            // canon, deviates from normal naming scheme
                            parameterName = "type";
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "field";
                        fptype = (Plugin.FilePropertyType)parameters[parameterName];
                        result = mbApiInterface.Pending_GetFileProperty(fptype);
                        break;
                    case "Pending_GetFileTag": // string (MetaDataType field)
                        field = (Plugin.MetaDataType)parameters["field"];
                        result = mbApiInterface.Pending_GetFileTag(field);
                        break;
            // api version 26
                    case "Player_GetButtonEnabled": // bool (PlayButtonType button)
                        button = (Plugin.PlayButtonType)parameters["button"];
                        result = mbApiInterface.Player_GetButtonEnabled(button);
                        break;
            // api version 27
                    // not canon, but fits 'To' for index functions
                    case "NowPlayingList_MoveFilesTo": // bool (int[] fromIndices, int toIndex)
                    // canon
                    case "NowPlayingList_MoveFiles": // bool (int[] fromIndices, int toIndex)
                        // cast the arraylist explictly to int and convert to an array
                        fromIndices = ((System.Collections.ArrayList)parameters["fromIndices"]).Cast<int>().ToArray();
                        toIndex = (int)parameters["toIndex"];
                        result = mbApiInterface.NowPlayingList_MoveFiles(fromIndices, toIndex);
                        break;
                    // fixes erratic behavior and bugs in indexing of Playlist_MoveFiles
                    case "NowPlayingList_MoveFiles_BK": // bool (string playlistUrl, int[] fromIndices, int toIndex)
                    // fits 'To' for index functions
                    case "NowPlayingList_MoveFilesTo_BK": // bool (string playlistUrl, int[] fromIndices, int toIndex)
                        fromIndices = ((System.Collections.ArrayList)parameters["fromIndices"]).Cast<int>().ToArray();
                        toIndex = (int)parameters["toIndex"];

                        // get a local copy of the playlist
                        filenames = new string[0];
                        mbApiInterface.NowPlayingList_QueryFilesEx("<SmartPlaylist />", ref filenames);
                        playlist = filenames.ToList();

                        // if from index out of bounds, fail
                        if (fromIndices.Max() > playlist.Count - 1)
                        {
                            result = false;
                            break;
                        }

                        // if trying to insert beyond the remaining list, append instead
                        uniqueIndices = new SortedSet<int>(fromIndices);
                        if (toIndex + uniqueIndices.Count > playlist.Count)
                        {
                            toIndex = playlist.Count - uniqueIndices.Count;
                        }

                        // copy items to move
                        copiedItems = new List<string>();
                        foreach (int i in fromIndices)
                        {
                            copiedItems.Add(filenames[i]);
                        }

                        // remove copied items
                        foreach (int i in uniqueIndices.Reverse())
                        {
                            playlist.RemoveAt(i);
                        }

                        // insert copied items
                        playlist.InsertRange(toIndex, copiedItems);
                        mbApiInterface.NowPlayingList_Clear();
                        foreach (string s in playlist)
                        {
                            mbApiInterface.NowPlayingList_QueueLast(s);
                        }
                        result = true;
                        break;
            // api version 28
                    case "Library_GetArtworkUrl": // string (string sourceFileUrl, int index)
                        throw new Exception("Library_GetArtworkUrl is meaningless or impractical in web context. Use Library_GetArtworkUrlLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces Library_GetArtworkUrl for web API
                    case "Library_GetArtworkUrlLink_BK": // string (string sourceFileUrl, int index)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        index = (int)parameters["index"];
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.Library_GetArtworkUrl(sourceFileUrl, index);
                        result = UrlToLink(filename);
                        break;
                    case "Library_GetArtistPictureThumb": // string (string artistName)
                        throw new Exception("Library_GetArtistPictureThumb is meaningless or impractical in web context. Use Library_GetArtistPictureThumbLink_BK to retrieve image.");
                    // Unique to Beekeeper, replaces Library_GetArtistPictureThumb for web API
                    case "Library_GetArtistPictureThumbLink_BK": // string (string artistName)
                        artistName = (string)parameters["artistName"];
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.Library_GetArtistPictureThumb(artistName);
                        result = UrlToLink(filename);
                        break;
                    case "NowPlaying_GetArtworkUrl": // string ()
                        throw new Exception("NowPlaying_GetArtworkUrl is meaningless or impractical in web context. Use NowPlaying_GetArtworkUrlLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces Library_GetArtworkUrl for web API
                    case "NowPlaying_GetArtworkUrlLink_BK": // string ()
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.NowPlaying_GetArtworkUrl();
                        result = UrlToLink(filename);
                        break;
                    case "NowPlaying_GetDownloadedArtworkUrl": // string ()
                        throw new Exception("NowPlaying_GetDownloadedArtworkUrl is meaningless or impractical in web context. Use NowPlaying_GetDownloadedArtworkUrlLink_BK to retrieve image.");
            // Unique to Beekeeper, replaces Library_GetArtworkUrl for web API
                    case "NowPlaying_GetDownloadedArtworkUrlLink_BK": // string ()
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.NowPlaying_GetDownloadedArtworkUrl();
                        result = UrlToLink(filename);
                        break;
            // api version 28
                    case "NowPlaying_GetArtistPictureThumb": // string (string artistName)
                        throw new Exception("NowPlaying_GetArtistPictureThumb is meaningless or impractical in web context. Use NowPlaying_GetArtistPictureThumbLink_BK to retrieve image.");
                    // Unique to Beekeeper, replaces NowPlaying_GetArtistPictureThumb for web API
                    case "NowPlaying_GetArtistPictureThumbLink_BK": // string (string artistName)
                        // MusicBee returns file location for (temporary) image file
                        filename = mbApiInterface.NowPlaying_GetArtistPictureThumb();
                        result = UrlToLink(filename);
                        break;
            // api version 29
                    case "Playlist_IsInList": // bool (string playlistUrl, string filename)
                        playlistUrl = (string)parameters["playlistUrl"];
                        if (parameters.ContainsKey("sourceFileUrl"))
                            // canon, deviates from normal naming scheme
                            parameterName = "sourceFileUrl";
                        else
                            // not canon, but fits normal naming scheme
                            parameterName = "filename";
                        filename = (string)parameters[parameterName];
                        result = mbApiInterface.Playlist_IsInList(playlistUrl, filename);
                        break;
            // api version 30
                    case "Library_GetArtistPictureUrls": // string[] (string artistName, bool localOnly)
                        // TODO Fix local url's 
                        artistName = (string)parameters["artistName"];
                        localOnly = (bool)parameters["localOnly"];
                        urls = new string[0];
                        mbApiInterface.Library_GetArtistPictureUrls(artistName, localOnly, ref urls);
                        // convert all (local) urls to weblinks (following MusicBee naming)
                        result = urls.Select(UrlToLink);
                        break;
                    case "NowPlaying_GetArtistPictureUrls": // string[] (bool localOnly)
                        localOnly = (bool)parameters["localOnly"];
                        urls = new string[0];
                        mbApiInterface.NowPlaying_GetArtistPictureUrls(localOnly, ref urls);
                        // convert all (local) urls to weblinks (following MusicBee naming)
                        result = urls.Select(UrlToLink);
                        break;
            // api version 31
                    case "Playlist_AppendFiles": // bool (string playlistUrl, string[] filenames)
                        if (!ReadOnly)
                        {
                            playlistUrl = (string)parameters["playlistUrl"];
                            // cast the arraylist explictly to string and convert to an array
                            if (parameters.ContainsKey("filenames"))
                                // canon, deviates from normal naming scheme
                                parameterName = "filenames";
                            else
                                // not canon, but fits normal naming scheme
                                parameterName = "sourceFileUrls";
                            filenames = ((System.Collections.ArrayList)parameters[parameterName]).Cast<string>().ToArray();
                            result = mbApiInterface.Playlist_AppendFiles(playlistUrl, filenames);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
            // api version 32
                    case "Sync_FileStart": // string (string filename)
                        filename = (string)parameters["filename"];
                        result = mbApiInterface.Sync_FileStart(filename);
                        break;
                    case "Sync_FileEnd": // void (string filename, bool success, string errorMessage)
                        filename = (string)parameters["filename"];
                        success = (bool)parameters["success"];
                        errorMessage = (string)parameters["errorMessage"];
                        mbApiInterface.Sync_FileEnd(filename, success, errorMessage);
                        result = true;
                        break;
            // api version 33
                    case "Library_QueryFilesEx": // string[] (string query)
                        query = (string)parameters["query"];
                        files = new string[0];
                        mbApiInterface.Library_QueryFilesEx(query, ref files);
                        result = files;
                        break;
                    case "NowPlayingList_QueryFilesEx": // string[] (string query)
                        query = (string)parameters["query"];
                        files = new string[0];
                        mbApiInterface.NowPlayingList_QueryFilesEx(query, ref files);
                        result = files;
                        break;
                    case "Playlist_QueryFilesEx": // string[] (playlistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        filenames = new string[0];
                        mbApiInterface.Playlist_QueryFilesEx(playlistUrl, ref filenames);
                        result = filenames;
                        break;
            // not canon, but fits 'To' for index functions
                    case "Playlist_MoveFilesTo": // bool (string playlistUrl, int[] fromIndices, int toIndex)
            // canon
                    case "Playlist_MoveFiles": // bool (string playlistUrl, int[] fromIndices, int toIndex)
                        if (!ReadOnly)
                        {
                            playlistUrl = (string)parameters["playlistUrl"];
                            fromIndices = ((System.Collections.ArrayList)parameters["fromIndices"]).Cast<int>().ToArray();
                            toIndex = (int)parameters["toIndex"];
                            result = mbApiInterface.Playlist_MoveFiles(playlistUrl, fromIndices, toIndex);
                        }
                        else
                        {
                            result = null;
                        }
                        break;
            // fixes erratic behavior and bugs in indexing of Playlist_MoveFiles
                    case "Playlist_MoveFiles_BK": // bool (string playlistUrl, int[] fromIndices, int toIndex)
            // fits 'To' for index functions
                    case "Playlist_MoveFilesTo_BK": // bool (string playlistUrl, int[] fromIndices, int toIndex)
                        if (!ReadOnly)
                        {
                            // TODO create a generic to do the heavy lifting for this function and NowPlayingList_MoveFiles_BK
                            playlistUrl = (string)parameters["playlistUrl"];
                            fromIndices = ((System.Collections.ArrayList)parameters["fromIndices"]).Cast<int>().ToArray();
                            toIndex = (int)parameters["toIndex"];

                            // get a local copy of the playlist
                            filenames = new string[0];
                            mbApiInterface.Playlist_QueryFilesEx(playlistUrl, ref filenames);
                            playlist = filenames.ToList();

                            // if from index out of bounds, fail
                            if (fromIndices.Max() > playlist.Count - 1)
                            {
                                result = false;
                                break;
                            }

                            // if trying to insert beyond the remaining list, append instead
                            uniqueIndices = new SortedSet<int>(fromIndices);
                            if (toIndex + uniqueIndices.Count > playlist.Count)
                            {
                                toIndex = playlist.Count - uniqueIndices.Count;
                            }

                            // copy items to move
                            copiedItems = new List<string>();
                            foreach (int i in fromIndices)
                            {
                                copiedItems.Add(filenames[i]);
                            }

                            // remove copied items
                            foreach (int i in uniqueIndices.Reverse())
                            {
                                playlist.RemoveAt(i);
                            }

                            // insert copied items
                            playlist.InsertRange(toIndex, copiedItems);
                            mbApiInterface.Playlist_SetFiles(playlistUrl, playlist.ToArray());
                            result = true;
                        }
                        else
                        {
                            result = null;
                        }
                        break;
                    case "Playlist_PlayNow": // bool (string playlistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        result = mbApiInterface.Playlist_PlayNow(playlistUrl);
                        break;
                    case "NowPlaying_IsSoundtrack": // bool ()
                        result = mbApiInterface.NowPlaying_IsSoundtrack();
                        break;
                    case "NowPlaying_GetSoundtrackPictureUrls": // string[] (bool localOnly)
                        localOnly = (bool)parameters["localOnly"];
                        urls = new string[0];
                        mbApiInterface.NowPlaying_GetSoundtrackPictureUrls(localOnly, ref urls);
                        // convert all (local) urls to weblinks (following MusicBee naming)
                        result = urls.Select(UrlToLink);
                        break;
                    case "Library_GetDevicePersistentId": // string (string sourceFileUrl, DeviceIdType idType)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        idType = (Plugin.DeviceIdType)parameters["idType"];
                        result = mbApiInterface.Library_GetDevicePersistentId(sourceFileUrl, idType);
                        break;
                    case "Library_SetDevicePersistentId": // bool (string sourceFileUrl, DeviceIdType idType, string value)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        idType = (Plugin.DeviceIdType)parameters["idType"];
                        value = (string)parameters["value"];
                        result = mbApiInterface.Library_SetDevicePersistentId(sourceFileUrl, idType, value);
                        break;
                    case "Library_FindDevicePersistentId": // string[] (DeviceIdType idType, string[] ids)
                        idType = (Plugin.DeviceIdType)parameters["idType"];
                        // cast the arraylist explictly to string and convert to an array
                        ids = ((System.Collections.ArrayList)parameters["ids"]).Cast<string>().ToArray();
                        values = new string[0];
                        mbApiInterface.Library_FindDevicePersistentId(idType, ids, ref values);
                        result = values;
                        break;
                    case "Setting_GetValue": // object (SettingId settingId)
                        settingId = (Plugin.SettingId)parameters["settingId"];
                        mbApiInterface.Setting_GetValue(settingId, ref result);
                        break;
                    case "Library_AddFileToLibrary": // string (string sourceFileUrl, LibraryCategory category)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        category = (Plugin.LibraryCategory)parameters["category"];
                        result = mbApiInterface.Library_AddFileToLibrary(sourceFileUrl, category);
                        break;
                    case "Playlist_DeletePlaylist": // bool (string playlistUrl)
                        playlistUrl = (string)parameters["playlistUrl"];
                        result = mbApiInterface.Playlist_DeletePlaylist(playlistUrl);
                        break;
                    case "Library_GetSyncDelta": // SyncDelta (string[] cachedFiles, DateTime updatedSince, LibraryCategory category)
                        // cast the arraylist explictly to string and convert to an array
                        cachedFiles = ((System.Collections.ArrayList)parameters["cachedFiles"]).Cast<string>().ToArray();
                        updatedSince = (DateTime)parameters["updatedSince"];
                        category = (Plugin.LibraryCategory)parameters["category"];
                        syncDelta = new SyncDelta();
                        syncDelta.newFiles = new string[0];
                        syncDelta.updatedFiles = new string[0];
                        syncDelta.deletedFiles = new string[0];
                        mbApiInterface.Library_GetSyncDelta(cachedFiles, updatedSince, category, ref syncDelta.newFiles, ref syncDelta.updatedFiles, ref syncDelta.deletedFiles);
                        result = syncDelta;
                        break;
            // api version 35
                    case "Library_GetFileTags": // strings[] (string sourceFileUrl, MetaDataType[] fields)
                        sourceFileUrl = (string)parameters["sourceFileUrl"];
                        // cast the arraylist parameters["fields"] explictly to the MetaDataType enum and convert to an array
                        fields = ((System.Collections.ArrayList)parameters["fields"]).Cast<Plugin.MetaDataType>().ToArray();
                        results = new string[0];
                        mbApiInterface.Library_GetFileTags(sourceFileUrl, fields, ref results);
                        result = results;
                        break;
                    case "NowPlaying_GetFileTags": // (MetaDataType[] fields)
                        fields = ((System.Collections.ArrayList)parameters["fields"]).Cast<Plugin.MetaDataType>().ToArray();
                        results = new string[0];
                        mbApiInterface.NowPlaying_GetFileTags(fields, ref results);
                        result = results;
                        break;
                    case "NowPlayingList_GetFileTags": // string[] (int index, MetaDataType[] fields)
                        index = (int)parameters["index"];
                        fields = ((System.Collections.ArrayList)parameters["fields"]).Cast<Plugin.MetaDataType>().ToArray();
                        results = new string[0];
                        mbApiInterface.NowPlayingList_GetFileTags(index, fields, ref results);
                        result = results;
                        break;
            // api version 43
                    case "MB_AddTreeNode": // ()
                        throw new Exception("MB_AddTreeNode is meaningless or impractical in web context. No replacement available.");
                    case "MB_DownloadFile": // ()
                        url = (string)parameters["url"];
                        target = (Plugin.DownloadTarget)parameters["target"];
                        targetFolder = (string)parameters["targetFolder"];
                        cancelDownload = (bool)parameters["cancelDownload"];
                        mbApiInterface.MB_DownloadFile(url, target, targetFolder, cancelDownload);
                        break;
                    default:
                        // no response, report failure
                        return false;
                }
            }
            catch (Exception e)
            {
                // if an exception is raised, report the error with a status 500
                response.StatusAndReason = HTTPServerResponse.HTTPStatus.HTTP_INTERNAL_SERVER_ERROR;
                using (Stream s = response.Send())
                using (TextWriter tw = new StreamWriter(s))
                {
                    tw.WriteLine(e.Message);
                }
                // a response was generated, so report success
                return true;
            }
            // output the result to the response
            response.StatusAndReason = HTTPServerResponse.HTTPStatus.HTTP_OK;
            using (Stream s = response.Send())
            using (TextWriter tw = new StreamWriter(s))
            {
                var jss = new JavaScriptSerializer();
                tw.WriteLine(jss.Serialize(result));
            }
            // report success
            return true;
        }

        /// <summary>
        /// This class implements IHTTPRequestHandler and is used in the factory RequestHandlerFactory.
        /// </summary>
        class BeekeeperHandler : IHTTPRequestHandler
        {
            public event EventsRequest eventsRequest;
            public event FileRequest fileRequest;
            public event TempRequest tempRequest;
            public event CallRequest callRequest;
            public event StatusRequest statusRequest;
            public event RegisterSession registerSession;

            /// <summary>
            /// Called when a situation arises that should not normally arise.
            /// </summary>
            /// <param name="response">The HTTPServerResponse on which the response should be sent.</param>
            private void InternalServerError(HTTPServerResponse response)
            {
                response.StatusAndReason = HTTPServerResponse.HTTPStatus.HTTP_INTERNAL_SERVER_ERROR;
                response.Send();
            }

            /// <summary>
            /// This method examines the incoming request and decides how it should be handled, passing off
            /// the handling to the right event or handling it itself.
            /// </summary>
            /// <param name="request">The current HTTPServerRequest.</param>
            /// <param name="response">The HTTPServerResponse on which the response should be sent.c</param>
            public void HandleRequest(HTTPServerRequest request, HTTPServerResponse response)
            {
                // get query parameters into a seperate string, remove from location
                int i = request.URI.IndexOf('?');
                string location;
                string parameters;
                if (i == -1)
                {
                    location = request.URI;
                }
                else
                {
                    parameters = request.URI.Remove(0, i + 1);
                    location = request.URI.Substring(0, i);
                }

                // make sure response shares session with request, for long polling calls
                registerSession(request, response);

                switch (location)
                {
                    case "/":
                    case "/index.html":
                        response.ContentType = "text/html";
                        using (Stream s = response.Send())
                        using (TextWriter tw = new StreamWriter(s))
                        {
                            tw.Write(Properties.Resources.index);
                        }
                        break;
                    case "/_base.css":
                        response.ContentType = "text/css";
                        using (Stream s = response.Send())
                        using (TextWriter tw = new StreamWriter(s))
                        {
                            tw.Write(Properties.Resources._base);
                        }
                        break;
                    case "/layout.css":
                        response.ContentType = "text/css";
                        using (Stream s = response.Send())
                        using (TextWriter tw = new StreamWriter(s))
                        {
                            tw.Write(Properties.Resources.layout);
                        }
                        break;
                    case "/skeleton.css":
                        response.ContentType = "text/css";
                        using (Stream s = response.Send())
                        using (TextWriter tw = new StreamWriter(s))
                        {
                            tw.Write(Properties.Resources.skeleton);
                        }
                        break;
                    case "/beekeeper.ico":
                        response.ContentType = "image/ico";
                        using (Stream s = response.Send())
                        {
                            Properties.Resources.beekeeper.Save(s);
                        }
                        break;
                    case "/favicon.ico":
                        response.ContentType = "image/ico";
                        using (Stream s = response.Send())
                        {
                            Properties.Resources.favicon.Save(s);
                        }
                        break;
                    case statusPath:
                        // at this point, it should be assigned, but check anyway to avoid silent death
                        if (statusRequest != null)
                        {
                            // deal with a request for a status record
                            statusRequest(response);
                        }
                        else
                        {
                            InternalServerError(response);
                        }
                        break;
                    case longPollingPath:
                        // at this point, it should be assigned, but check anyway to avoid silent death
                        if (eventsRequest != null)
                        {
                            // deal with a request for events (long polling call, if no events are queued for this session
                            eventsRequest(response);
                        }
                        else
                        {
                            InternalServerError(response);
                        }
                        break;
                    default:
                        // if the location starts with filePath/, have the file request handler deal with it
                        if (location.StartsWith(filePath+'/'))
                        {
                            fileRequest(response, location.Substring(6));
                        }
                        else
                        {
                            if (location.StartsWith(picturePath+'/'))
                            {
                                tempRequest(response, location.Substring(9));
                            }
                            else
                            {
                                // only option left is that it's a function call on the API itself
                                using (Stream s = request.GetRequestStream())
                                using (StreamReader sr = new StreamReader(s))
                                {
                                    var jss = new JavaScriptSerializer();
                                    // handle the call with the name of the method (without '/') and send the 
                                    // body of the request as parameters, assuming a POST
                                    string str = sr.ReadToEnd();
                                    if (!callRequest(response, location.Substring(1), jss.Deserialize<Dictionary<string, object>>(str)))
                                    {
                                        response.StatusAndReason = HTTPServerResponse.HTTPStatus.HTTP_NOT_FOUND;
                                        response.Send();
                                    }
                                }
                            }
                        }
                        break;
                }
            }
        }

        class RequestHandlerFactory : IHTTPRequestHandlerFactory
        {
            public event EventsRequest eventsRequest;
            public event FileRequest fileRequest;
            public event TempRequest tempRequest;
            public event CallRequest callRequest;
            public event StatusRequest statusRequest;
            public event RegisterSession registerSession;

            public IHTTPRequestHandler CreateRequestHandler(HTTPServerRequest request)
            {
                BeekeeperHandler bh = new BeekeeperHandler();
                bh.eventsRequest += this.eventsRequest;
                bh.fileRequest += this.fileRequest;
                bh.tempRequest += this.tempRequest;
                bh.callRequest += this.callRequest;
                bh.statusRequest += this.statusRequest;
                bh.registerSession += this.registerSession;
                return bh;
            }
        }
        
        private RequestHandlerFactory factory;
        private HTTPServer server;

        public BeekeeperServer(IntPtr apiInterfacePtr)
        {
            mbApiInterface = (Plugin.MusicBeeApiInterface)Marshal.PtrToStructure(apiInterfacePtr, typeof(Plugin.MusicBeeApiInterface));

            factory = new RequestHandlerFactory();
            factory.eventsRequest += this.HandleEventsRequest;
            factory.fileRequest += this.HandleFileRequest;
            factory.tempRequest += this.HandleTempRequest;
            factory.callRequest += this.HandleCallRequest;
            factory.statusRequest += this.HandleStatusRequest;
            factory.registerSession += this.HandleRegisterSession;

            _Sessions = new Dictionary<string, Session>();

            tempUrls = new HashSet<string>();
        }

        // Flag: Has Dispose already been called? 
        bool disposed = false;

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                Stop();
                server.Dispose();
            }
        }

/*      replaced with the above implementation of IDisposable
 * 
        ~BeekeeperServer()
        {
            Stop();
        } */

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Stop()
        {
            if (_IsRunning)
            {
                server.Stop();
                _IsRunning = false;
            }
        }

        /// <summary>
        /// Set the port for the server and start it.
        /// </summary>
        public void SetPortAndStart(int port)
        {
            Stop();

            _Port = port;

            server = new HTTPServer(factory, _Port);
            server.Start();
            _IsRunning = true;
        }

        /// <summary>
        /// Equeue a MusicBeeEvent on all queues of active sessions.
        /// </summary>
        /// <remarks>Active sessions are sessions in _Sessions.</remarks>
        /// <param name="name">Name of the event as it is passed in the event.</param>
        /// <param name="parameters">Parameters as passed to the MusicBeeEvent constructor.</param>
        /// <seealso cref="MusicBeeEvent"/>
        public void EnqueueEvent(string name, params object[] parameters)
        {
            foreach (KeyValuePair<string, Session> kvp in _Sessions)
            {
                Session ses = kvp.Value;
                ses.events.Enqueue(new MusicBeeEvent(name, parameters));
                if (ses.eventsRequestMRE != null)
                {
                    ses.eventsRequestMRE.Set();
                    ses.eventsRequestMRE = null;
                }
            }
        }
    }

    public class PluginSettings
    {
        private string path;
        
        // property port with default, getter and setter
        private int _Port = 8080;
        public int Port
        {
            get
            {
                return this._Port;
            }
            set
            {
                this._Port = value;
            }
        }
        // property run with default, getter and setter
        private Boolean _Run = true;
        public Boolean Run
        {
            get
            {
                return this._Run;
            }
            set
            {
                this._Run = value;
            }
        }
        // property run with default, getter and setter
        private Boolean _Share = true;
        public Boolean Share
        {
            get
            {
                return this._Share;
            }
            set
            {
                this._Share = value;
            }
        }
        // property run with default, getter and setter
        private Boolean _ReadOnly = true;
        public Boolean ReadOnly
        {
            get
            {
                return this._ReadOnly;
            }
            set
            {
                this._ReadOnly = value;
            }
        }

        public PluginSettings(string settingsPath)
        {
            this.path = settingsPath + "\\Beekeeper.ini";
            try
            {
                using (StreamReader sr = new StreamReader(this.path))
                {
                    this._Port = Convert.ToUInt16(sr.ReadLine());
                    this._Run = Convert.ToBoolean(sr.ReadLine());
                    this._Share = Convert.ToBoolean(sr.ReadLine());
                    this._ReadOnly = Convert.ToBoolean(sr.ReadLine());
                    sr.Close();
                }
            }
            catch (Exception)
            { 
                // defaults are used
            }
        }

        public void SaveSettings()
        {
            using (StreamWriter sw = new StreamWriter(this.path))
            {
                sw.WriteLine(Convert.ToString(this._Port));
                sw.WriteLine(Convert.ToString(this._Run));
                sw.WriteLine(Convert.ToString(this._Share));
                sw.WriteLine(Convert.ToString(this._ReadOnly));
                sw.Close();
            }
        }
    }

    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        private BeekeeperServer beekeeperServer;
        private PluginSettings settings;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = (MusicBeeApiInterface)Marshal.PtrToStructure(apiInterfacePtr, typeof(MusicBeeApiInterface));

            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "Beekeeper";
            about.Description = "MusicBee web service for Beekeeper for Android.";
            about.Author = "grismar@grismar.net";
            about.TargetApplication = "";
            about.Type = PluginType.General;
            about.VersionMajor = 1;  // plugin version
            about.VersionMinor = 0;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 85;  // not implemented yet: height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            // load settings or set defaults
            settings = new PluginSettings(mbApiInterface.Setting_GetPersistentStoragePath());
            // create a beekeeper server
            beekeeperServer = new BeekeeperServer(apiInterfacePtr);
            beekeeperServer.about = about;

            return about;
        }

        // event handler for dealing with changed port number (on leaving the field, since there is no apply event?)
        private void textBox_Leave(object sender, EventArgs e, CheckBox runningCheckBox)
        {
            int port = Convert.ToUInt16(((TextBox)sender).Text);
            try
            {
                beekeeperServer.SetPortAndStart(port);
                settings.Port = port;
            }
            catch
            {
                MessageBox.Show("Unable to start on port " + ((TextBox)sender).Text + ". Try a different port number.");
                ((TextBox)sender).Text = Convert.ToString(settings.Port);
            }
        }

        // event handler for dealing with starting or stopping the server as a result of changing the checkbox
        private void checkBoxRunning_CheckedChanged(object sender, EventArgs e)
        {
            Boolean run = ((CheckBox)sender).Checked;
            if (run)
            {
                beekeeperServer.SetPortAndStart(settings.Port);
            }
            else
            {
                beekeeperServer.Stop();
            }
            // set the checkbox and settings value to match actual server state
            ((CheckBox)sender).Checked = beekeeperServer.IsRunning;
            settings.Run = beekeeperServer.IsRunning;
        }

        // event handler for dealing with enabling file serving as result of changing the Share checkbox
        private void checkBoxShare_CheckedChanged(object sender, EventArgs e)
        {
            beekeeperServer.Share = ((CheckBox)sender).Checked;
            // set the checkbox and settings value to match actual server state
            ((CheckBox)sender).Checked = beekeeperServer.Share;
            settings.Share = beekeeperServer.Share;
        }

        // event handler for dealing with enabling file serving as result of changing the ReadOnly checkbox
        private void checkBoxReadOnly_CheckedChanged(object sender, EventArgs e)
        {
            beekeeperServer.ReadOnly = ((CheckBox)sender).Checked;
            // set the checkbox and settings value to match actual server state
            ((CheckBox)sender).Checked = beekeeperServer.ReadOnly;
            settings.ReadOnly = beekeeperServer.ReadOnly;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // panelHandle will only be set if about.ConfigurationPanelHeight is set to a non-zero value
            // the panel width is scaled according to the font the user has selected
            // if about.ConfigurationPanelHeight is set to 0, display own popup window
            if (panelHandle != IntPtr.Zero)
            {
                // create controls here
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "Beekeeper server port (8080 default) : ";
                TextBox textBox = new TextBox();
                int left = TextRenderer.MeasureText(prompt.Text, configPanel.Font).Width;
                textBox.Bounds = new Rectangle(left + 10, 0, left + 75, textBox.Height);
                textBox.Width = TextRenderer.MeasureText("88888", configPanel.Font).Width;
                textBox.Text = Convert.ToString(settings.Port);
                CheckBox checkBoxRunning = new CheckBox();
                checkBoxRunning.Checked = beekeeperServer.IsRunning;
                checkBoxRunning.Text = "Service running";
                checkBoxRunning.Bounds = new Rectangle(0, textBox.Height + 5, TextRenderer.MeasureText(checkBoxRunning.Text, configPanel.Font).Width + 25, textBox.Height + 5);
                checkBoxRunning.CheckedChanged += checkBoxRunning_CheckedChanged;
                textBox.Leave += new EventHandler((sender, e) => textBox_Leave(sender, e, checkBoxRunning));
                CheckBox checkBoxShare = new CheckBox();
                checkBoxShare.Checked = beekeeperServer.Share;
                checkBoxShare.Text = "Serving shared files";
                checkBoxShare.Bounds = new Rectangle(0, textBox.Height + checkBoxRunning.Height + 5, TextRenderer.MeasureText(checkBoxShare.Text, configPanel.Font).Width + 25, textBox.Height + 5);
                checkBoxShare.CheckedChanged += checkBoxShare_CheckedChanged;
                CheckBox checkBoxReadOnly = new CheckBox();
                checkBoxReadOnly.Checked = beekeeperServer.ReadOnly;
                checkBoxReadOnly.Text = "Don't allow web API calls to modify MusicBee database (read only)";
                checkBoxReadOnly.Bounds = new Rectangle(0, textBox.Height + checkBoxRunning.Height + checkBoxShare.Height + 5, TextRenderer.MeasureText(checkBoxReadOnly.Text, configPanel.Font).Width + 40, textBox.Height + 5);
                checkBoxShare.CheckedChanged += checkBoxReadOnly_CheckedChanged;
                configPanel.Controls.AddRange(new Control[] { prompt, textBox, checkBoxRunning, checkBoxShare, checkBoxReadOnly });
                configPanel.Height = textBox.Height + checkBoxRunning.Height + checkBoxShare.Height + checkBoxReadOnly.Height + 10;
            }
            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            settings.SaveSettings();
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            beekeeperServer.Stop();
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            string[] strings;
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                    // start server
                    try
                    {
                        beekeeperServer.Share = settings.Share;
                        // if the server should run according to settings
                        if (settings.Run)
                        {
                            beekeeperServer.SetPortAndStart(settings.Port);

                            beekeeperServer.EnqueueEvent("PluginStartup");
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Unable to start Beekeeper service. Try changing the configured port number.");
                    }
                    break;
                case NotificationType.TrackChanging:
                    beekeeperServer.EnqueueEvent("TrackChanging");
                    break;
                case NotificationType.TrackChanged:
                    beekeeperServer.EnqueueEvent("TrackChanged", new Dictionary<string, dynamic>(){ { "fileURL", mbApiInterface.NowPlaying_GetFileUrl() } });
                    break;
                case NotificationType.PlayStateChanged:
                    beekeeperServer.EnqueueEvent("PlayStateChanged", new Dictionary<string, dynamic>() { 
                        { "playState", mbApiInterface.Player_GetPlayState() },
                        { "position", mbApiInterface.Player_GetPosition() }
                    });
                    break;
                case NotificationType.AutoDjStarted:
                    beekeeperServer.EnqueueEvent("AutoDjStarted");
                    break;
                case NotificationType.AutoDjStopped:
                    beekeeperServer.EnqueueEvent("AutoDjStopped");
                    break;
                case NotificationType.VolumeMuteChanged:
                    beekeeperServer.EnqueueEvent("VolumeMuteChanged", new Dictionary<string, dynamic>() { { "mute", mbApiInterface.Player_GetMute() } });
                    break;
                case NotificationType.VolumeLevelChanged:
                    beekeeperServer.EnqueueEvent("VolumeLevelChanged", new Dictionary<string, dynamic>() { { "volume", mbApiInterface.Player_GetVolume() } });
                    break;
                case NotificationType.NowPlayingListChanged:
                    mbApiInterface.NowPlayingList_QueryFiles(null);
                    strings = new string[0];
                    // select everything in the Now Playing List
                    mbApiInterface.NowPlayingList_QueryFilesEx("<SmartPlaylist><Source Type=\"1\" /></SmartPlaylist>", ref strings);
                    beekeeperServer.EnqueueEvent("NowPlayingListChanged", new Dictionary<string, dynamic>() { { "nowPlayingAllFiles", strings } });
                    break;
                case NotificationType.NowPlayingListEnded:
                    beekeeperServer.EnqueueEvent("NowPlayingListEnded");
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    beekeeperServer.EnqueueEvent("NowPlayingArtworkReady");
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    beekeeperServer.EnqueueEvent("NowPlayingLyricsReady");
                    break;
                case NotificationType.TagsChanging:
                    beekeeperServer.EnqueueEvent("TagsChanging");
                    break;
                case NotificationType.TagsChanged:
                    beekeeperServer.EnqueueEvent("TagsChanged");
                    break;
                case NotificationType.RatingChanging:
                    beekeeperServer.EnqueueEvent("RatingChanging");
                    break;
                case NotificationType.RatingChanged:
                    beekeeperServer.EnqueueEvent("RatingChanged");
                    break;
                case NotificationType.PlayCountersChanged:
                    beekeeperServer.EnqueueEvent("PlayCountersChanged", new Dictionary<string, dynamic>() { { "fileURL", mbApiInterface.NowPlaying_GetFileUrl() } });
                    break;
                case NotificationType.ScreenSaverActivating:
                    beekeeperServer.EnqueueEvent("ScreenSaverActivating");
                    break;
                case NotificationType.ShutdownStarted:
                    beekeeperServer.EnqueueEvent("ShutdownStarted");
                    break;
                case NotificationType.EmbedInPanel:
                    beekeeperServer.EnqueueEvent("EmbedInPanel");
                    break;
                case NotificationType.PlayerRepeatChanged:
                    beekeeperServer.EnqueueEvent("PlayerRepeatChanged", new Dictionary<string, dynamic>() { { "repeat", mbApiInterface.Player_GetRepeat() } });
                    break;
                case NotificationType.PlayerShuffleChanged:
                    beekeeperServer.EnqueueEvent("PlayerShuffleChanged", new Dictionary<string, dynamic>() { { "shuffle", mbApiInterface.Player_GetShuffle() } });
                    break;
                case NotificationType.PlayerEqualiserOnOffChanged:
                    beekeeperServer.EnqueueEvent("PlayerEqualiserOnOffChanged", new Dictionary<string, dynamic>() { { "equaliserEnabled", mbApiInterface.Player_GetEqualiserEnabled() } });
                    break;
                case NotificationType.PlayerScrobbleChanged:
                    beekeeperServer.EnqueueEvent("PlayerScrobbleChanged", new Dictionary<string, dynamic>() { { "scrobbleEnabled", mbApiInterface.Player_GetScrobbleEnabled() } });
                    break;
                case NotificationType.ReplayGainChanged:
                    beekeeperServer.EnqueueEvent("ReplayGainChanged", new Dictionary<string, dynamic>() { { "replayGainMode", mbApiInterface.Player_GetReplayGainMode() } });
                    break;
                case NotificationType.FileDeleting:
                    beekeeperServer.EnqueueEvent("FileDeleting");
                    break;
                case NotificationType.FileDeleted:
                    beekeeperServer.EnqueueEvent("FileDeleted");
                    break;
                case NotificationType.ApplicationWindowChanged:
                    beekeeperServer.EnqueueEvent("ApplicationWindowChanged");
                    break;
                case NotificationType.StopAfterCurrentChanged:
                    beekeeperServer.EnqueueEvent("StopAfterCurrentChanged", new Dictionary<string, dynamic>() { { "stopAfterCurrentEnabled", mbApiInterface.Player_GetStopAfterCurrentEnabled() } });
                    break;
                case NotificationType.LibrarySwitched:
                    beekeeperServer.EnqueueEvent("LibrarySwitched");
                    break;
                case NotificationType.FileAddedToLibrary:
                    beekeeperServer.EnqueueEvent("FileAddedToLibrary");
                    break;
                case NotificationType.FileAddedToInbox:
                    beekeeperServer.EnqueueEvent("FileAddedToInbox");
                    break;
                case NotificationType.SynchCompleted:
                    beekeeperServer.EnqueueEvent("SynchCompleted");
                    break;
                case NotificationType.DownloadCompleted:
                    beekeeperServer.EnqueueEvent("DownloadCompleted");
                    break;
                case NotificationType.MusicBeeStarted:
                    beekeeperServer.EnqueueEvent("MusicBeeStarted");
                    break;
            }
        }

        #region " Lyrics Plugin "

        public string[] GetProviders()
        {
            // PluginType != LyricsRetrieval
            return null;
        }

        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        {
            // PluginType != LyricsRetrieval
            return null;
        }

        #endregion

        #region " Artwork Plugin "

        public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album, string provider)
        {
            // PluginType != ArtworkRetrieval
            return null;
        }

        #endregion

        #region " Storage Plugin "
        // user initiated refresh (eg. pressing F5) - reconnect/ clear cache as appropriate
        public void Refresh()
        {
        }

        // is the server ready
        // you can initially return false and then use MB_SendNotification when the storage is ready (or failed)
        public bool IsReady()
        {
            return false;
        }

        // return a 16x16 bitmap for the storage icon
        public Bitmap GetIcon()
        {
            return new Bitmap(16, 16);
        }

        public bool FolderExists(string path)
        {
            return true;
        }
        
        // return the full path of folders in a folder
        public string[] GetFolders(string path)
        {
            return new string[]{};
        }

        // this function returns an array of files in the specified folder
        // each file is represented as a array of tags - each tag being a KeyValuePair(Of Byte, String), where Byte is a FilePropertyType or MetaDataType enum value and String is the value
        // a tag for FilePropertyType.Url must be included
        // you can initially return null and then use MB_SendNotification when the file data is ready (on receiving the notification MB will call GetFiles(path) again)
        public KeyValuePair<byte, string>[][] GetFiles(string path)
        {
            return null;
        }

        public bool FileExists(string url)
        {
            return true;
        }

        //  each file is represented as a array of tags - each tag being a KeyValuePair(Of Byte, String), where Byte is a FilePropertyType or MetaDataType enum value and String is the value
        // a tag for FilePropertyType.Url must be included
        public KeyValuePair<byte, string>[] GetFile(string url)
        {
            return null;
        }
        
        // return an array of bytes for the raw picture data
        public byte[] GetFileArtwork(string url )
        {
            return null;
        }

        // return an array of playlist identifiers
        // where each playlist identifier is a KeyValuePair(id, name)
        public KeyValuePair<string, string>[] GetPlaylists()
        {
            return null;
        }

        // return an array of files in a playlist - a playlist being identified by the id parameter returned by GetPlaylists()
        // each file is represented as a array of tags - each tag being a KeyValuePair(Of Byte, String), where Byte is a FilePropertyType or MetaDataType enum value and String is the value
        // a tag for FilePropertyType.Url must be included
        public KeyValuePair<byte, string>[][] GetPlaylistFiles(string id)
        {
            return null;
        }

        // return a stream that can read through the raw (undecoded) bytes of a music file
        public System.IO.Stream GetStream(string url)
        {
            return null;
        }

        // return the last error that occurred
        public  Exception GetError()
        {
            return null;
        }

        #endregion
    }
}