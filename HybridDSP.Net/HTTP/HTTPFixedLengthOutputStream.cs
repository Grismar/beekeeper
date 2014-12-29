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
using System.IO;
using System.Text;

namespace HybridDSP.Net.HTTP
{
    internal class HTTPFixedLengthOutputStream : Stream
    {
        private HTTPServerSession _session;
        private long _maxLength;

        public HTTPFixedLengthOutputStream(HTTPServerSession session, long maxLength)
        {
            _session = session;
            _maxLength = maxLength;
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override void Flush()
        { }

        public override long Length
        {
            get { throw new Exception("The method or operation is not supported."); }
        }

        public override long Position
        {
            get
            {
                throw new Exception("The method or operation is not supported.");
            }
            set
            {
                throw new Exception("The method or operation is not supported.");
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new Exception("The method or operation is not supported.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new Exception("The method or operation is not supported.");
        }

        public override void SetLength(long value)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > _maxLength)
            {
                _session.Write(buffer, offset, (int)_maxLength);
                _maxLength = 0;
                throw new HTTPException("No more data can be sent");
            }
            else if (_maxLength > 0)
            {
                _session.Write(buffer, offset, count);
                _maxLength -= count;
            }
            else if (count > 0)
                throw new HTTPException("No more data can be sent");
        }
    }
}
