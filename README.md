Beekeeper
=========

## Synopsis

At the top of the file there should be a short introduction and/ or overview that explains **what** the project is. This description should match descriptions added for package managers (Gemspec, package.json, etc.)
Beekeeper MusicBee plugin to expose Web API. 

The project consists of a plugin .dll (mb_beekeeper.dll) with a supporting .dll (HybridDSP.Net.dll) and a JavaScript library with example code. The .dll's comprise the MusicBee plugin and work independently from the Javascript library. The beekeeper.js Javascript library is ready to use (in combination with jQuery and JSON2) and provides additional documentation for the API. The examples show how to use all of the individual API methods through beekeeper.js.

## Code Example

Assuming you've installed and configured the plugin in MusicBee:

	<script type="text/javascript" src="jquery.js"></script>
    <script type="text/javascript" src="json2.js"></script>
    <script type="text/javascript" src="beekeeper.js"></script>
    <script type="text/javascript">
        $(document).ready(function() {
		    Beekeeper.basePath = "http://musicbee_pc:8080/";

			Beekeeper.Setting_GetPersistentStoragePath(
                    function(data){ alert( data ); }
            );
		}
	</script>

## Motivation

MusicBee, the freeware music player and library fow Windows has a plugin API that allows plugin developers access to many, if not most, of the internal functions of the program. However, this API has been written with C# .Net developers in mind. To be able to access MusicBee from any platform capable to make http-requests, Beekeeper was developed.

Beekeeper exposes the same API as a web API. It includes a simple embedded web server that exposes the same methods, it supports a long polling interface for catching events and serves required files. The interface of Beekeeper has been (and will be) kept as close to the MusicBee API as possible and sensible from a web API perspective.

## Installation

Download and install MusicBee from http://getmusicbee.com.

Download and install Beekeeper from http://grismar.net/beekeeper (forthcoming).

In MusicBee, under Edit - Preference - Plugins, check that beekeeper is available, that the selected port is to your liking (it should be free) and that 'Service running' is checked. You may need to restart MusicBee, after hitting "Apply" and "Save".

If you need Beekeeper to host your web application as well, make sure that "Serving shared files" is checked and that your files are in the MusicBee persistent storage folder. And if you don't want Beekeeper to change anything directly in the music library, check "Don't allow web API calls to modify MusicBee database (read only)".

By default, you should be able to see that Beekeeper is running at http://localhost:8080/ on the MusicBee system.

## API Reference

The easiest way to get a handle on the methods and events in the Beekeeper web API, is to read https://github.com/Grismar/beekeeper/blob/master/files/beekeeper.js

## Contributors

I'm not actively looking for contributors, but if you do want to reach me, you can find me at @grismar on Twitter, mail me at grismar@grismar.net or create issues on the GitHub page.

## License

The Beekeeper source itself is licensed under Apache 2.0. Please note that this license does not extend to jQuery (The jQuery Foundation, open-source MIT license), JSON2 (Douglas Crockford, public domain), HybridDSP.Net (Hybrid GeoTools, Apache 2.0 license) and skeleton.css (Dave Gamache, open-source MIT license).

You can find more about those components at (respectively) http://jquery.com, https://github.com/douglascrockford/JSON-js, http://www.codeproject.com/Articles/20445/C-Customizable-Embedded-HTTPServer and http://getskeleton.com/.
