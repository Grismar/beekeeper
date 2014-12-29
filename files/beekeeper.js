/**
 * Beekeeper module containing types and methods for easy access to the Beekeeper MusicBee Plugin.
 * Requires jquery (for $.ajax) and JSON (for JSON.Stringify) to be available.
 *
 * This version was written for MusicBee 2.4.5404, API version 43.
 *
 * Future compatibility of the Beekeeper Plugin with MusicBee cannot be guaranteed and is at the
 * mercy of the author of MusicBee.
 *
 * @module beekeeper
 * @version 1.0
 * @requires jquery
 * @requires json
 * @copyright Jaap van der Velde 2014
 * @license Apache-2.0
 *
 * Disclaimer: other than being written specifically for interaction with the MusicBee application,
 * there exist no links between MusicBee and Beekeeper, nor between their makers. Use Beekeeper at
 * your own risk. No guarantee is offered, no responsibility is taken for any damage or other
 * adverse effects of use of Beekeeper, intended or otherwise.
 */

var Beekeeper = {
// Definition of MusicBee specific data types
// Note: the comments for Web API methods will mention the data type when applicable
    SkinElement: {
        SkinInputControl: 7,
        SkinInputPanel: 10,
        SkinInputPanelLabel: 14,
        SkinTrackAndArtistPanel: -1
    },

    ElementState: {
        ElementStateDefault: 0,
        ElementStateModified: 6
    },

    ElementComponent: {
        ComponentBorder: 0,
        ComponentBackground: 1,
        ComponentForeground: 3
    },

    FilePropertyType: {
        Url: 2,
        Kind: 4,
        Format: 5,
        Size: 7,
        Channels: 8,
        SampleRate: 9,
        Bitrate: 10,
        DateModified: 11,
        DateAdded: 12,
        LastPlayed: 13,
        PlayCount: 14,
        SkipCount: 15,
        Duration: 16,
        Status: 21,
        NowPlayingListIndex: 78,  // only has meaning when called from NowPlayingList_* commands
        ReplayGainTrack: 94,
        ReplayGainAlbum: 95
    },
    
    MetaDataType : {
        TrackTitle: 65,
        Album: 30,
        AlbumArtist: 31,        // displayed album artist
        AlbumArtistRaw: 34,     // stored album artist
        Artist: 32,             // displayed artist
        MultiArtist: 33,        // individual artists, separated by a null char
        PrimaryArtist: 19,      // first artist from multi-artist tagged file, otherwise displayed artist
        Artists: 144,
        ArtistsWithArtistRole: 145,
        ArtistsWithPerformerRole: 146,
        ArtistsWithGuestRole: 147,
        ArtistsWithRemixerRole: 148,
        Artwork: 40,
        BeatsPerMin: 41,
        Composer: 43,           // displayed composer
        MultiComposer: 89,      // individual composers, separated by a null char
        Comment: 44,
        Conductor: 45,
        Custom1: 46,
        Custom2: 47,
        Custom3: 48,
        Custom4: 49,
        Custom5: 50,
        Custom6: 96,
        Custom7: 97,
        Custom8: 98,
        Custom9: 99,
        Custom10: 128,
        Custom11: 129,
        Custom12: 130,
        Custom13: 131,
        Custom14: 132,
        Custom15: 133,
        Custom16: 134,
        DiscNo: 52,
        DiscCount: 54,
        Encoder: 55,
        Genre: 59,
        Genres: 143,
        GenreCategory: 60,
        Grouping: 61,
        Keywords: 84,
        HasLyrics: 63,
        Lyricist: 62,
        Lyrics: 114,
        Mood: 64,
        Occasion: 66,
        Origin: 67,
        Publisher: 73,
        Quality: 74,
        Rating: 75,
        RatingLove: 76,
        RatingAlbum: 104,
        Tempo: 85,
        TrackNo: 86,
        TrackCount: 87,
        Virtual1: 109,
        Virtual2: 110,
        Virtual3: 111,
        Virtual4: 112,
        Virtual5: 113,
        Virtual6: 122,
        Virtual7: 123,
        Virtual8: 124,
        Virtual9: 125,
        Virtual10: 135,
        Virtual11: 136,
        Virtual12: 137,
        Virtual13: 138,
        Virtual14: 139,
        Virtual15: 140,
        Virtual16: 141,
        Year: 88
    },

    LyricsType: {
        NotSpecified: 0,
        Synchronised: 1,
        UnSynchronised: 2
    },

    PlayState: {
        Undefined: 0,
        Loading: 1,
        Playing: 3,
        Paused: 6,
        Stopped: 7
    },

    RepeatMode: {
        None: 0,
        All: 1,
        One: 2
    },

    PlaylistFormat: {
        Unknown: 0,
        M3u: 1,
        Xspf: 2,
        Asx: 3,
        Wpl: 4,
        Pls: 5,
        Auto: 7,
        M3uAscii: 8,
        AsxFile: 9,
        Radio: 10,
        M3uExtended: 11,
        Mbp: 12
    },

    ReplayGainMode: {
        Off: 0,
        Track: 1,
        Album: 2,
        Smart: 3
    },

    DataType: {
        String: 0,
        Number: 1,
        DateTime: 2,
        Rating: 3
    },

    /**
     * Generic Call function, invokes method 'name' with data 'input' and calls 'callback' function with output data
     * @param {string} name Name of method to be called
     * @param {object} input Serializable JSON object with input parameters for method
     * @param {function} callback Callback function for method, expecting a JSON object
     */
    Call: function(name, input, callback) {
        $.ajax({
	        url: "/"+name,
	        type: "POST",
	        data: JSON.stringify(input),
	        processData: false,
	        dataType: "json"
	    })
        .done(callback)
        .fail(function(jqXHR) { callback({ error: "Request failed: " + jqXHR.responseText }); });
    },

// Definition of supported MusicBee methods

    /**
     * Retrieves the persistent storage path for MusicBee. Path is reported to callback.
     * Setting_GetPersistentStoragePath (function (string) callback)
     * Note: this path is exposed as /file/ when 'Serving shared files' is enabled in plugin settings
     * @param {function} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetPersistentStoragePath: function(callback) {
        this.Call(
	        'Setting_GetPersistentStoragePath',
	        {},
	        callback
	    );
    },

    /**
     * Retrieves the path to the active skin. File path is reported to callback.
     * Setting_GetSkin (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetSkin: function(callback) {
        this.Call(
            'Setting_GetSkin',
            {},
            callback
        )
    },

    /**
     * Retrieves the specific color of an element in the active skin. Color is reported to callback.
     * Setting_GetSkinElementColour (SkinElement element, ElementState state, ElementComponent component,
     *   function (string) callback)
     * @param {int} element Window element to get color for
     * @param {int} state State in which the element has the color
     * @param {int} component Component of the element to get the color for
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetSkinElementColour: function(element, state, component, callback) {
      this.Call(
          'Setting_GetSkinElementColour',
          { element: element, state: state, component: component },
          callback
      )
    },

    /**
     * @see Setting_GetSkinElementColour (canon)
     * Function defined to match overall American English spelling scheme
     * @param {int} element Window element to get color for
     * @param {int} state State in which the element has the color
     * @param {int} component Component of the element to get the color for
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetSkinElementColor: function(element, state, component, callback) {
        this.Setting_GetSkinElementColour(element, state, component, callback);
    },

    /**
     * Retrieves whether the Window Borders for MusicBee are skinned. Result is reported to callback.
     * Setting_IsWindowBordersSkinned (function (boolean) callback)
     * @param {function} callback Callback function for method, expecting a (boolean) JSON object
     */
    Setting_IsWindowBordersSkinned: function(callback) {
        this.Call(
            'Setting_IsWindowBordersSkinned',
            {},
            callback
        )
    },

    /**
     * Retrieves a single property for the give source file. Property is reported to callback.
     * Library_GetFileProperty (string sourceFileUrl, FilePropertyType type, function (string) callback)
     * @param {string} sourceFileUrl
     * @param {int} type
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Library_GetFileProperty: function(sourceFileUrl, type, callback) {
        this.Call(
            'Library_GetFileProperty',
            { sourceFileUrl: sourceFileUrl, type: type },
            callback
        )
    },

    /**
     * Retrieves a single metadata field (tag) for the given source file. Tag is reported to callback.
     * Library_GetFileTag (string sourceFileUrl, MetaDataType field, function (string) callback)
     * @param {string} sourceFileUrl Path to music file in database
     * @param {string} field Name of field to retrieve
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Library_GetFileTag: function(sourceFileUrl, field, callback) {
        this.Call(
	        'Library_GetFileTag',
	        { sourceFileUrl : sourceFileUrl, field: field },
	        callback
	    );
    },

    /**
     * Sets a single metadata field (tag) for the given source file. Success is reported to callback.
     * Library_SetFileTag (string sourceFileUrl, MetaDataType field, object value,
     *   function (boolean) callback)
     * @param {string} sourceFileUrl Path to music file in database
     * @param {string} field Name of field to set
     * @param {object} value Value to set field to
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Library_SetFileTag: function(sourceFileUrl, field, value, callback) {
        this.Call(
            'Library_SetFileTag',
            { sourceFileUrl: sourceFileUrl, field: field, value: value },
            callback
        )
    },

    /**
     * Commits changes to tags on a source file to the file. Success is reported to callback.
     * Library_CommitTagsToFile (string sourceFileUrl, function (boolean) callback)
     * @param {string} sourceFileUrl Path to music file in database
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Library_CommitTagsToFile: function(sourceFileUrl, callback) {
        this.Call(
            'Library_CommitTagsToFile',
            { sourceFileUrl: sourceFileUrl },
            callback
        )
    },

    /**
     * Retrieves lyrics from a given source file. Lyrics are reported to callback.
     * Library_GetLyrics (string sourceFileUrl, LyricsType type, function (string) callback)
     * @param {string} sourceFileUrl Path to music file in database
     * @param {int} type Type of lyrics to retrieve (synchronised or not, or unspecified)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Library_GetLyrics: function(sourceFileUrl, type, callback) {
        this.Call(
            'Library_GetLyrics',
            { sourceFileUrl: sourceFileUrl, type: type },
            callback
        )
    },

    /**
     * Retrieves artwork from a given source file. Image data is passed to callback as base64-encoded jpg.
     * Library_GetArtwork (string sourceFileUrl, int index, function (string) callback)
     * @param {string} sourceFileUrl Path to music file in database
     * @param {int} index Index of image in source file (in case of multiple images, starting at 0)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Library_GetArtwork: function(sourceFileUrl, index, callback) {
        this.Call(
            'Library_GetArtwork',
            { sourceFileUrl: sourceFileUrl, index: index },
            callback
        )
    },

    /**
     * Starts a query to retrieve files/tracks from library; @see Library_QueryGetNextFile.
     * Library_QueryFiles (string query, function (boolean) callback)
     * @param {string} query Query string to start query with; either "domain=SelectedFiles", "domain=DisplayedFiles"
     *   or an auto playlist xml document
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Library_QueryFiles: function(query, callback) {
        this.Call(
            'Library_QueryFiles',
            { query: query },
            callback
        )
    },

    /**
     * Returns the next result from a query started with @see Library_QueryFiles. File url is reported to callback.
     * Library_QueryGetNextFile (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Library_QueryGetNextFile: function(callback) {
        this.Call(
            'Library_QueryGetNextFile',
            {},
            callback
        )
    },

    /**
     * Get the position of the currently playing file (if any). Position is reported to callback.
     * Player_GetPosition (function (int) callback)
     * @param {function (object)} callback Callback function for method, expecting a (int) JSON object
     */
    Player_GetPosition: function(callback) {
        this.Call(
            'Player_GetPosition',
            {},
            callback
        )
    },

    /**
     * Set the position of the currently playing file (if any). Success is reported to callback.
     * Player_GetPosition (int position, function (boolean) callback)
     * @param {int} position The position (in milliseconds) in the currently playing file to set the player
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetPosition: function(position, callback) {
        this.Call(
            'Player_SetPosition',
            { position: position },
            callback
        )
    },

    /**
     * Retrieves the current play state of the MusicBee player. Play state is reported to callback.
     * Player_GetPlayState (function (PlayState output) callback)
     * @param {function (object)} callback Callback function for method, expecting a (PlayState) JSON object
     */
    Player_GetPlayState: function(callback) {
        this.Call(
            'Player_GetPlayState',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to play or pause if it was playing. Success is reported to callback.
     * Player_PlayPause (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_PlayPause: function(callback) {
        this.Call(
            'Player_PlayPause',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to stop any playback. Success is reported to callback.
     * Player_Stop (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_Stop: function(callback) {
        this.Call(
            'Player_Stop',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to stop playback once the playing track completes. Success is reported to callback.
     * Player_StopAfterCurrent (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_StopAfterCurrent: function(callback) {
        this.Call(
            'Player_StopAfterCurrent',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to start playing the previous track in now playing. Success is reported to callback.
     * Player_PlayPreviousTrack (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_PlayPreviousTrack: function(callback) {
        this.Call(
            'Player_PlayPreviousTrack',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to start playing the next track in now playing. Success is reported to callback.
     * Player_PlayNextTrack (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_PlayNextTrack: function(callback) {
        this.Call(
            'Player_PlayNextTrack',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to enter Auto DJ mode. Success is reported to callback.
     * Player_StartAutoDj (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_StartAutoDj: function(callback) {
        this.Call(
            'Player_StartAutoDj',
            {},
            callback
        )
    },

    /**
     * Causes the MusicBee player to exit Auto DJ mode. Success is reported to callback.
     * Player_StartAutoDj (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_EndAutoDj: function(callback) {
        this.Call(
            'Player_EndAutoDj',
            {},
            callback
        )
    },

    /**
     * Retrieves the current volume setting of the MusicBee player. Current volume is reported to callback.
     * Player_GetVolume (function (float) callback)
     * @param {function (object)} callback Callback function for method, expecting a (float) JSON object
     */
    Player_GetVolume: function(callback) {
        this.Call(
            'Player_GetVolume',
            {},
            callback
        )
    },

    /**
     * Sets the current volume of the MusicBee player. Success is reported to callback.
     * Player_SetVolume (float volume, function (boolean) callback)
     * @param {number} volume What value to set the volume to (0..1)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetVolume: function(volume, callback) {
        this.Call(
            'Player_SetVolume',
            { volume: volume },
            callback
        )
    },

    /**
     * Retrieves whether or not the MusicBee player is muted. Success is reported to callback.
     * Player_GetMute (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetMute: function(callback) {
        this.Call(
            'Player_GetMute',
            {},
            callback
        )
    },

    /**
     * Sets whether or not the MusicBee player is muted. Success is reported to callback.
     * Player_SetMute (bool mute, function (boolean) callback)
     * @param {boolean} mute Muted (true) or not (false)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetMute: function(mute, callback) {
        this.Call(
            'Player_SetMute',
            { mute: mute },
            callback
        )
    },

    /**
     * Retrieves whether or not the MusicBee player is in shuffle mode. Shuffle mode is reported to callback.
     * Player_GetShuffle (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetShuffle: function(callback) {
        this.Call(
            'Player_GetShuffle',
            {},
            callback
        )
    },

    /**
     * Sets whether or not the MusicBee player is in shuffle mode. Success is reported to callback.
     * Player_SetShuffle (boolean shuffle, function (boolean) callback)
     * @param {boolean} shuffle In shuffle mode (true) or not (false)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetShuffle: function(shuffle, callback) {
        this.Call(
            'Player_SetShuffle',
            { shuffle: shuffle },
            callback
        )
    },

    /**
     * Retrieves whether or not the MusicBee player is in repeat mode. Repeat mode is reported to callback.
     * Player_GetRepeat (function (RepeatMode) callback)
     * @param {function (object)} callback Callback function for method, expecting a (RepeatMode) JSON object
     */
    Player_GetRepeat: function(callback) {
        this.Call(
            'Player_GetRepeat',
            {},
            callback
        )
    },

    /**
     * Sets whether or not the MusicBee player is in repeat mode. Success is reported to callback.
     * Player_SetRepeat (RepeatMode repeat, function (boolean) callback)
     * @param {number} repeat Repeat none, all (in now playing) or the current one.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetRepeat: function(repeat, callback) {
        this.Call(
            'Player_SetRepeat',
            { repeat: repeat },
            callback
        )
    },

    /**
     * Retrieves whether the equaliser is enabled.
     * Player_GetEqualiserEnabled(function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetEqualiserEnabled: function(callback) {
        this.Call(
            'Player_GetEqualiserEnabled',
            {},
            callback
        )
    },

    /**
     * @see Player_GetEqualiserEnabled (canon)
     * Function defined to match naming conventions of API
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetEqualizerEnabled: function (callback) {
        this.Player_GetEqualiserEnabled(callback);
    },

    /**
     * Sets whether the equaliser is enabled.
     * Player_SetEqualiserEnabled(boolean enabled, function (boolean) callback)
     * @param {boolean} enabled Whether or not the equaliser should be enabled.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetEqualiserEnabled: function(enabled, callback) {
        this.Call(
            'Player_SetEqualiserEnabled',
            { enabled: enabled},
            callback
        )
    },

    /**
     * @see Player_SetEqualiserEnabled (canon)
     * Function defined to match naming conventions of API
     * @param {boolean} enabled Whether or not the equaliser should be enabled.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetEqualizerEnabled: function (enabled, callback) {
        this.Player_SetEqualiserEnabled(enabled, callback);
    },


    /**
     * Retrieves whether digital signal processing effects are applied.
     * Player_GetDspEnabled(function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetDspEnabled: function(callback) {
        this.Call(
            'Player_GetDspEnabled',
            {},
            callback
        )
    },

    /**
     * Sets whether digital signal processing effects are applied.
     * Player_SetDspEnabled(boolean enabled, function (boolean) callback)
     * @param {boolean} enabled Whether or not digital signal processing effects should be applied.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetDspEnabled: function(enabled, callback) {
        this.Call(
            'Player_SetDspEnabled',
            { enabled: enabled },
            callback
        )
    },

    /**
     * Retrieves whether Last.fm scrobbling is enabled.
     * Player_GetScrobbleEnabled(function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetScrobbleEnabled: function(callback) {
        this.Call(
            'Player_GetScrobbleEnabled',
            {},
            callback
        )
    },

    /**
     * Sets whether Last.fm scrobbling is enabled.
     * Player_SetScrobbleEnabled(boolean enabled, function (boolean) callback)
     * @param {boolean} enabled Whether or not Last.fm scrobbling should be enabled.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetScrobbleEnabled: function(enabled, callback) {
        this.Call(
            'Player_SetScrobbleEnabled',
            { enabled: enabled },
            callback
        )
    },

    /**
     * Gets the file url for the current track.
     * NowPlaying_GetFileUrl(function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetFileUrl: function(callback) {
        this.Call(
            'NowPlaying_GetFileUrl',
            {},
            callback
        )
    },

    /**
     * Gets the duration (in milliseconds) for the current track.
     * NowPlaying_GetDuration (function (int) callback)
     * @param {function (object)} callback Callback function for method, expecting a (int) JSON object
     */
    NowPlaying_GetDuration: function(callback) {
        this.Call(
            'NowPlaying_GetDuration',
            {},
            callback
        )
    },

    /**
     * Gets the value of a file property for the current track.
     * NowPlaying_GetFileProperty (FilePropertyType type, function (string) callback)
     * @param {int} type Specific FilePropertyType to retrieve
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetFileProperty: function(type, callback) {
        this.Call(
            'NowPlaying_GetFileProperty',
            { type: type },
            callback
        )
    },

    /**
     * Get the value of a metadata tag for the current track.
     * NowPlaying_GetFileTag (MetaDataType field, function (string) callback)
     * @param {int} field Specific MetaDataType to retrieve
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetFileTag: function(field, callback) {
        this.Call(
            'NowPlaying_GetFileTag',
            { field: field },
            callback
        )
    },

    /**
     * Get any available lyrics for the current track.
     * NowPlaying_GetLyrics (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetLyrics: function(callback) {
        this.Call(
            'NowPlaying_GetLyrics',
            {},
            callback
        )
    },

    /**
     * Get artwork for the current track. Image data is passed to callback as base64-encoded jpg.
     * NowPlaying_GetArtwork (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetArtwork: function(callback) {
        this.Call(
            'NowPlaying_GetArtwork',
            {},
            callback
        )
    },

    /**
     * Clears the Now Playing List. Doesn't clear 'current track', will continue playing and re-added when restarted.
     * NowPlayingList_Clear (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_Clear: function(callback) {
        this.Call(
            'NowPlayingList_Clear',
            {},
            callback
        )
    },

    /**
     * Runs a query against the Now Playing list (including tracks not in library).
     * Follow up with calls to NowPlayingList_QueryGetNextFile.
     * NowPlayingList_QueryFiles (string query, function (boolean) callback)
     * @param {string} query Query string to start query with; either "domain=SelectedFiles", "domain=DisplayedFiles"
     *   or an auto playlist xml document
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_QueryFiles: function(query, callback) {
        this.Call(
            'NowPlayingList_QueryFiles',
            { query: query },
            callback
        )
    },

    /**
     * Retrieves the next result from the active query on Now Playing list, started by @see NowPlayingList_QueryFiles.
     * NowPlayingList_QueryGetNextFile (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlayingList_QueryGetNextFile: function(callback) {
        this.Call(
            'NowPlayingList_QueryGetNextFile',
            {},
            callback
        )
    },

    /**
     * Replaces the Now Playing list with a specific track and starts playing it.
     * NowPlayingList_PlayNow (string sourceFileUrl, function (boolean) callback)
     * @param {string} sourceFileUrl The path to the track (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_PlayNow: function(sourceFileUrl, callback) {
        this.Call(
            'NowPlayingList_PlayNow',
            { sourceFileUrl: sourceFileUrl },
            callback
        )
    },

    /**
     * Enqueues a specific track right after the current track (inserting it) on the Now Playing list.
     * NowPlayingList_QueueNext (string sourceFileUrl, function (boolean) callback)
     * @param {string} sourceFileUrl The path to the track (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_QueueNext: function(sourceFileUrl, callback) {
        this.Call(
            'NowPlayingList_QueueNext',
            { sourceFileUrl: sourceFileUrl },
            callback
        )
    },

    /**
     * Enqueues a specific track at the end of (appending it to) the Now Playing list.
     * NowPlayingList_QueueLast (string sourceFileUrl, function (boolean) callback)
     * @param {string} sourceFileUrl The path to the track (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_QueueLast: function(sourceFileUrl, callback) {
        this.Call(
            'NowPlayingList_QueueLast',
            { sourceFileUrl: sourceFileUrl },
            callback
        )
    },

    /**
     * Play random tracks from library. Drops entire library in Now Playing List, enables shuffle and starts playback.
     * NowPlayingList_PlayLibraryShuffled (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_PlayLibraryShuffled: function(callback) {
        this.Call(
            'NowPlayingList_PlayLibraryShuffled',
            {},
            callback
        )
    },

    /**
     * Runs a query for playlists in MusicBee. Follow up with calls to @see Playlist_QueryGetNextPlaylist.
     * Playlist_QueryPlaylists (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Playlist_QueryPlaylists: function(callback) {
        this.Call(
            'Playlist_QueryPlaylists',
            {},
            callback
        )
    },

    /**
     * Retrieves the next result from the active query for playlists, started by @see Playlist_QueryPlaylists.
     * Playlist_QueryGetNextPlaylist (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Playlist_QueryGetNextPlaylist: function(callback) {
        this.Call(
            'Playlist_QueryGetNextPlaylist',
            {},
            callback
        )
    },

    /**
     * Retrieves the format of a given playlist.
     * Playlist_GetType (string playlistUrl, function (PlaylistFormat) callback)
     * @param {string} playlistUrl The path to the playlist (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (PlaylistFormat) JSON object
     * @constructor
     */
    Playlist_GetType: function (playlistUrl, callback) {
        this.Call(
            'Playlist_GetType',
            { playlistUrl: playlistUrl },
            callback
        )
    },

    /**
     * @see Playlist_GetType (canon)
     * Function defined to match naming conventions of API
     * @param {string} playlistUrl The path to the playlist (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (PlaylistFormat) JSON object
     */
    Playlist_GetFormat: function (playlistUrl, callback) {
        this.Playlist_GetType(playlistUrl, callback);
    },

    /**
     * Runs a query for all files/tracks in a specific playlist. Follow up with calls to @see Playlist_QueryGetNextFile.
     * @param {string} playlistUrl The path to the playlist (from MusicBee local perspective).
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Playlist_QueryFiles: function (playlistUrl, callback) {
        this.Call(
            'Playlist_QueryFiles',
            { playlistUrl: playlistUrl },
            callback
        )
    },

    /**
     * Retrieves next result from the active query for files/tracks on a playlist, started by @see Playlist_QueryFiles.
     * Playlist_QueryGetNextPlaylist (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Playlist_QueryGetNextFile: function (callback) {
        this.Call(
            'Playlist_QueryGetNextFile',
            {},
            callback
        )
    },

    /**
     * Causes MusicBee to refresh user interface panels.
     * MB_RefreshPanels(function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    MB_RefreshPanels: function(callback) {
        this.Call(
            'MB_RefreshPanels',
            {},
            callback
        )
    },

    /**
     * Retrieves the field/tag name for a specific MetaDataType.
     * Setting_GetFieldName (MetaDataType field, function (string) callback)
     * @param {int} field The MetaDataType for which to retrieve the name.
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetFieldName: function(field, callback) {
        this.Call(
            'Setting_GetFieldName',
            { field: field },
            callback
        )
    },

    /**
     * Retrieves the default Font name for the current skin in MusicBee.
     * Note: not part of the original MusicBee Plugin API (not canon), replacing Setting_GetDefaultFont.
     * Setting_GetDefaultFontName_BK (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    Setting_GetDefaultFontName_BK: function(callback) {
        this.Call(
            'Setting_GetDefaultFontName_BK',
            {},
            callback
        )
    },

    /**
     * Retrieves the file url (from MusicBee local perspective) for the index-th file/track on the Now Playing list.
     * NowPlayingList_GetListFileUrl (int index, function (string) callback)
     * @param {int} index The index of the item on the list, with 0 being the top one.
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlayingList_GetListFileUrl: function(index, callback) {
        this.Call(
            'NowPlayingList_GetListFileUrl',
            { index: index },
            callback
        )
    },

    /**
     * Retrieve the vale of a file property for the index-th file/track on the Now Playing list.
     * NowPlayingList_GetFileProperty (int index, FilePropertyType type, function (string) callback)
     * @param {int} index The index of the item on the list, with 0 being the top one.
     * @param {int} type The FilePropertyType of the property to retrieve.
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlayingList_GetFileProperty: function(index, type, callback) {
        this.Call(
            'NowPlayingList_GetFileProperty',
            { index: index, type: type },
            callback
        )
    },

    /**
     * Retrieve the vale of a file tag (metadata) for the index-th file/track on the Now Playing list.
     * NowPlayingList_GetFileTag (int index, MetaDataType type, function (string) callback)
     * @param {int} index The index of the item on the list, with 0 being the top one.
     * @param {int} field The MetaDataType of the field/tag to retrieve.
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlayingList_GetFileTag: function(index, field, callback) {
        this.Call(
            'NowPlayingList_GetFileTag',
            { index: index, field: field },
            callback
        )
    },

    /**
     * TODO function unknown, but works
     * @param id
     * @param defaultText
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    MB_GetLocalisation: function(id, defaultText, callback) {
        this.Call(
            'MB_GetLocalization',
            { id: id, defaultText: defaultText },
            callback
        )
    },

    /**
     * @see MB_GetLocalisation (canon)
     * Function defined to match naming conventions of API
     * @param id
     * @param defaultText
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    MB_GetLocalization: function (id, defaultText, callback) {
        this.MB_GetLocalisation(id, defaultText, callback);
    },

    /**
     * Retrieves whether there are any tracks in the Now Playing list prior to the current track.
     * NowPlayingList_IsAnyPriorTracks (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_IsAnyPriorTracks: function (callback) {
        this.Call(
            'NowPlayingList_IsAnyPriorTracks',
            {},
            callback
        )
    },

    /**
     * Retrieves whether there are any tracks in the Now Playing list following the current track.
     * NowPlayingList_IsAnyFollowingTracks (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    NowPlayingList_IsAnyFollowingTracks: function (callback) {
        this.Call(
            'NowPlayingList_IsAnyFollowingTracks',
            {},
            callback
        )
    },

    /**
     * Causes MusicBee to show the equalizer in its user interface.
     * Player_ShowEqualiser (function (boolean) callback)
     * Note: Currently defective (v1.0) and will only generate an error message from Beekeeper
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_ShowEqualiser: function (callback) {
        this.Call(
            'Player_ShowEqualiser',
            {},
            callback
        )
    },

    /**
     * @see Player_ShowEqualiser (canon)
     * Function defined to match naming conventions of API
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_ShowEqualizer: function (callback) {
        this.Player_ShowEqualiser(callback);
    },

    /**
     * Retrieves whether the AutoDj is enabled.
     * Player_GetAutoDjEnabled (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetAutoDjEnabled: function (callback) {
        this.Call(
            'Player_GetAutoDjEnabled',
            {},
            callback
        )
    },

    /**
     * Retrieves whether playback will stop after the current track (for example after @see Player_StopAfterCurrent).
     * Player_GetStopAfterCurrentEnabled (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetStopAfterCurrentEnabled: function (callback) {
        this.Call(
            'Player_GetStopAfterCurrentEnabled',
            {},
            callback
        )
    },

    /**
     * Retrieves whether MusicBee will crossfade from file/track to the next.
     * Player_GetCrossfade (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetCrossfade: function (callback) {
        this.Call(
            'Player_GetCrossfade',
            {},
            callback
        )
    },

    /**
     * Sets whether MusicBee should crossfade from file/track to the next.
     * Player_SetCrossfade (boolean crossfade, function (boolean) callback)
     * @param {boolean} crossfade Whether MusicBee should crossfade.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetCrossfade: function (crossfade, callback) {
        this.Call(
            'Player_SetCrossfade',
            { crossfade: crossfade },
            callback
        )
    },

    /**
     * Retrieves what replay gain mode is enabled for adjusting volume across playback of files/tracks.
     * Player_GetReplayGainMode (function (ReplayGainMode) callback)
     * @param {function (object)} callback Callback function for method, expecting a (ReplayGainMode) JSON object
     */
    Player_GetReplayGainMode: function (callback) {
        this.Call(
            'Player_GetReplayGainMode',
            {},
            callback
        )
    },

    /**
     * Sets whether replay gain mode should be enabled for relatively continuous volume across files/tracks.
     * Player_SetCrossfade (boolean mode, function (boolean) callback)
     * @param {int} mode Which ReplayGainMode MusicBee should use for replay gain.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_SetReplayGainMode: function (mode, callback) {
        this.Call(
            'Player_SetReplayGainMode',
            { mode: mode },
            callback
        )
    },

    /**
     * Causes MusicBee to enqueue a specified number of random files/tracks, at the end of the Now Playing list.
     * @param {int} count The number of files/tracks to enqueue.
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_QueueRandomTracks: function (count, callback) {
        this.Call(
            'Player_QueueRandomTracks',
            { count: count },
            callback
        )
    },

    /**
     * Retrieves the data type of the give MetaDataType (tag) as a MusicBee DataType.
     * @param {int} field MetaDataType for which to retrieve the field.
     * @param {function (object)} callback Callback function for method, expecting a (DataType) JSON object
     */
    Setting_GetDataType: function (field, callback) {
        this.Call(
            'Setting_GetDataType',
            { field: field },
            callback
        )
    },

    /**
     * Retrieves index in the Now Playing list of the track that will play as the offset-th file/track after current.
     * Note: effectively returns MaxInt (index of current file/track + offset, count (Now Playing) - 1)
     * @param {int} offset Distance from current track (0 retrieves index of current track, not next!)
     * @param {function (object)} callback Callback function for method, expecting a (int) JSON object
     */
    NowPlayingList_GetNextIndex: function (offset, callback) {
        this.Call(
            'NowPlayingList_GetNextIndex',
            { offset: offset },
            callback
        )
    },

    /**
     * Causes MusicBee to try and retrieve an image for the artist from the web, saving it in a temporary location.
     * The callback is passed a url that points to the image, which will be available until MusicBee is restarted,
     * or the image is no longer available to MusicBee.
     * Note: not part of the original MusicBee Plugin API (not canon), replacing NowPlaying_GetArtistPicture.
     * Note: the url points to a jpeg image, thought the file extension is typically .dat or .tmp
     * Note: apply fadingPercent = 0 to avoid temporary images being generated for every call
     * NowPlaying_GetArtistPictureUrl_BK (int fadingPercent, function (string) callback)
     * @param {int} fadingPercent A percentage by which the image is faded between 0% and 50% opaque. (0 for no change)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetArtistPictureUrl_BK: function (fadingPercent, callback) {
        this.Call(
            'NowPlaying_GetArtistPictureUrl_BK',
            { fadingPercent: fadingPercent },
            callback
        )
    },

    /**
     * Retrieves a web client usable url to downloaded artwork for the current file/track.
     * The callback is passed a url that points to the image, which will be available until MusicBee is restarted,
     * or the image is no longer available to MusicBee.
     * Note: not part of the original MusicBee Plugin API (not canon), replacing NowPlaying_GetDownloadedArtwork.
     * NowPlaying_GetDownloadedArtworkUrl_BK (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetDownloadedArtworkUrl_BK: function (callback) {
        this.Call(
            'NowPlaying_GetDownloadedArtworkUrl_BK',
            {},
            callback
        )
    },

    /**
     * Causes MusicBee to display the Now Playing assistant.
     * MB_ShowNowPlayingAssistant (function (boolean) callback)
     * Note: Currently defective (v1.0) and will only generate an error message from Beekeeper
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    MB_ShowNowPlayingAssistant: function (callback) {
        this.Call(
            'MB_ShowNowPlayingAssistant',
            {},
            callback
        )
    },

    /**
     * Retrieves any downloaded lyrics for the current file/track.
     * NowPlaying_GetDownloadedLyrics (function (string) callback)
     * @param {function (object)} callback Callback function for method, expecting a (string) JSON object
     */
    NowPlaying_GetDownloadedLyrics: function(callback) {
        this.Call(
            'NowPlaying_GetDownloadedLyrics',
            {},
            callback
        )
    },

    /**
     * Retrieves whether MusicBee displays star rating button and status for each track.
     * Player_GetShowRatingTrack (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetShowRatingTrack: function (callback) {
        this.Call(
            'Player_GetShowRatingTrack',
            {},
            callback
        )
    },

    /**
     * Retrieves whether MusicBee displays last.fm 'love' rating button and status for each track.
     * Player_GetShowRatingLove (function (boolean) callback)
     * @param {function (object)} callback Callback function for method, expecting a (boolean) JSON object
     */
    Player_GetShowRatingLove: function (callback) {
        this.Call(
            'Player_GetShowRatingLove',
            {},
            callback
        )
    },

    /**
     * Retrieves a set of metadata fields (tags) for the given source file. Array of tags is reported to callback.
     * Library_GetFileTags (string aSourceFileUrl, MetaDataType[] aFields, function (string[] output) callback)
     * @param {string} aSourceFileUrl Path to music file in database
     * @param {int[]} aFields Names of fields to retrieve
     * @param {function (object)} callback Callback function for method, expecting a (string[]) JSON object
     */
    Library_GetFileTags: function(aSourceFileUrl, aFields, callback) {
        this.Call(
            'Library_GetFileTags',
            { sourceFileUrl : aSourceFileUrl, fields: aFields },
            callback
        );
    },

    TestRun: function(playlistUrl, callback) {
        this.Call(
            'TestRun',
            { playlistUrl: playlistUrl },
            callback
        )
    }
};
