using System;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Exception thrown for OAuth authentication errors
    /// </summary>
    public class OAuthException : Exception
    {
        public string ErrorCode { get; }
        public string ErrorDescription { get; }

        public OAuthException(string errorCode, string errorDescription)
            : base(FormatMessage(errorCode, errorDescription))
        {
            ErrorCode = errorCode;
            ErrorDescription = errorDescription;
        }

        public OAuthException(string message) : base(message)
        {
            ErrorCode = "unknown_error";
            ErrorDescription = message;
        }

        public OAuthException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = "unknown_error";
            ErrorDescription = message;
        }

        private static string FormatMessage(string errorCode, string errorDescription)
        {
            if (string.IsNullOrEmpty(errorDescription))
                return errorCode;
            return $"{errorCode}: {errorDescription}";
        }
    }
}
