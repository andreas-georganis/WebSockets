using System;

namespace WebSockets.Client.Abstractions
{
    public class ErrorOccuredArgs : EventArgs
    {
        public ErrorOccuredArgs(Exception exception, string message)
        {
            Exception = exception;
            Message = message;
        }

        public Exception Exception {get;}

        public string Message {get;}
    }
}