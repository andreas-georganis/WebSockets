using System;

namespace WebSockets.Client.Abstractions
{
    public struct ConnectionClosedArgs
    {
        internal ConnectionClosedArgs(short code, string reason) : this()
        {
            Code = code;
            Reason = reason;
        }

        public short Code {get;}

        public string Reason {get;}
    }
}