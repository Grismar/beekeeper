/*
 * Copyright (c) 2007, Hybrid DSP
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Hybrid DSP nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY HYBRID DSP ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL HYBRID DSP BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
/// <author>grismar</author>
using System.Net;

namespace HybridDSP.Net.HTTP
{
    /// <summary>
    /// This class represents a request made by a client to the server.
    /// </summary>
    public class HTTPServerRequest : HTTPMessage
    {
        private HTTPServerSession _session;

        public const string HTTP_GET      = "GET";
        public const string HTTP_HEAD     = "HEAD";
        public const string HTTP_PUT      = "PUT";
        public const string HTTP_POST     = "POST";
        public const string HTTP_OPTIONS  = "OPTIONS";
        public const string HTTP_DELETE   = "DELETE";
        public const string HTTP_TRACE    = "TRACE";
        public const string HTTP_CONNECT  = "CONNECT";
        public const string HOST          = "Host";
        public const string COOKIE        = "Cookie";
        public const string AUTHORIZATION = "Authorization";

        /// <summary>
        /// Name of cookie used to store session ID.
        /// </summary>
        /// <author>grismar</author>
        public const string COOKIE_NAME = "HTTPServerRequestSessionID";

        private string _method;
        private string _uri;

        private Stream _stream = null;

        internal HTTPServerRequest(HTTPServerSession session)
        {
            _session = session;

            HTTPHeaderInputStream hs = new HTTPHeaderInputStream(session);
            Read(hs);
        }

        /// <summary>
        /// Gets the request method.
        /// </summary>
        public string Method
        {
            get { return _method; }
        }

        /// <summary>
        /// Gets the request URI.
        /// </summary>
        public string URI
        {
            get { return _uri; }
        }

        /// <summary>
        /// Gets if the request expects a continue. If so, the session will handle this
        /// internally.
        /// </summary>
        public bool ExpectsContinue
        {
            get
            {
                return Has("Expect") && string.Compare(Get("Expect"), "100-continue", true) == 0;
            }
        }

        /// <summary>
        /// Reads the request from a Stream.
        /// </summary>
        /// <param name="istr"></param>
        public override void Read(Stream istr)
        {
            string method = "";
            string uri = "";
            string version = "";

            int c = istr.ReadByte();
            if (c == EOF)
                throw new HTTPNoMessageException();
            while (char.IsWhiteSpace((char)c)) c = istr.ReadByte();
            if (c == EOF)
                throw new HTTPMessageException("No HTTP request header");
            while (!char.IsWhiteSpace((char)c) && c != EOF) { method += (char)c; c = istr.ReadByte(); }
            if (!char.IsWhiteSpace((char)c))
                throw new HTTPMessageException("HTTP request method invalid");
            while (char.IsWhiteSpace((char)c)) c = istr.ReadByte();
            while (!char.IsWhiteSpace((char)c) && c != EOF) { uri += (char)c; c = istr.ReadByte(); }
            if (!char.IsWhiteSpace((char)c))
                throw new HTTPMessageException("HTTP request URI invalid");
            while (char.IsWhiteSpace((char)c)) c = istr.ReadByte();
            while (!char.IsWhiteSpace((char)c) && c != EOF) { version += (char)c; c = istr.ReadByte(); }
            if (!char.IsWhiteSpace((char)c))
                throw new HTTPMessageException("HTTP version string invalid");
            while (c != '\n' && c != EOF) { c = istr.ReadByte(); }

            base.Read(istr);
            c = LastRead;
            while (c != '\n' && c != EOF) { c = istr.ReadByte(); }

            _method = method;
            _uri = uri;
            Version = version;
        }

        /// <summary>
        /// Gets a Stream that represents the body of the request.
        /// </summary>
        /// <returns></returns>
        public Stream GetRequestStream()
        {
            if (_stream != null)
                return _stream;

            if (ChunkedTransferEncoding)
                throw new HTTPException("Chunked transfer encoding is not (yet) supported");
            else if (ContentLength != UNKNOWN_CONTENT_LENGTH)
                _stream = new HTTPFixedLengthInputStream(_session, ContentLength);
            else if (Method == HTTP_GET || Method == HTTP_HEAD)
                _stream = new HTTPFixedLengthInputStream(_session, 0);
            else
                _stream = new HTTPInputStream(_session);

            Debug.Assert(_stream != null);
            return _stream;
        }

        /// <summary>
        /// Storage property for SessionID.
        /// </summary>
        /// <author>grismar</author>
        private string _SessionID = null;
        /// <summary>
        /// SessionID is a read-only property that returns the current session id.
        /// </summary>
        /// <remarks>If no session id is set, an attempt is made to retrieve it from a cookie, otherwise a new id is generated (GUID).</remarks>
        /// <author>grismar</author>
        public string SessionID
        {
            get
            {
                if (this._SessionID == null)
                {
                    if (!RetrieveSessionID())
                    {
                        this._SessionID = Guid.NewGuid().ToString();
                    }
                }
                return _SessionID;
            }
        }

        /// <summary>
        /// RetrieveSessionID attempts to retrieve a session id from a cookie.
        /// </summary>
        /// <returns>Success of attempt to retrieve session id from cookie.</returns>
        /// <author>grismar</author>
        public bool RetrieveSessionID()
        {
            string cookie = Get(COOKIE);
            string[] cookieCrumbs = cookie.Split(new string[] { "; " }, StringSplitOptions.None);
            foreach (string cookieCrumb in cookieCrumbs)
            {
                string[] keyValue = cookieCrumb.Split('=');
                if (keyValue[0] == COOKIE_NAME)
                {
                    _SessionID = keyValue[1];
                    return true;
                }
            }
            return false;
        }
    }
}
